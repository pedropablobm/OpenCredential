/*
	Copyright (c) 2011, pGina Team
	All rights reserved.

	Redistribution and use in source and binary forms, with or without
	modification, are permitted provided that the following conditions are met:
		* Redistributions of source code must retain the above copyright
		  notice, this list of conditions and the following disclaimer.
		* Redistributions in binary form must reproduce the above copyright
		  notice, this list of conditions and the following disclaimer in the
		  documentation and/or other materials provided with the distribution.
		* Neither the name of the pGina Team nor the names of its contributors 
		  may be used to endorse or promote products derived from this software without 
		  specific prior written permission.

	THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
	ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
	WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
	DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE FOR ANY
	DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
	(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
	LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
	ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
	(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
	SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
*/
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.ComponentModel;
using System.Threading;
using System.ServiceProcess;

using OpenCredential.Shared.Interfaces;
using OpenCredential.Shared.Settings;
using OpenCredential.Shared.Types;

using Abstractions.WindowsApi;
using log4net;

namespace OpenCredential.Plugin.DatabaseLogger
{
    enum LoggerMode { EVENT, SESSION };

    public class PluginImpl : IPluginConfiguration, IPluginEventNotifications
    {
        private sealed class ActiveSessionContext
        {
            public int WindowsSessionId { get; set; }
            public SessionProperties Properties { get; set; }
            public string SessionState { get; set; }
            public DateTime LastHeartbeatUtc { get; set; }
        }

        public static readonly Guid PluginUuid = new Guid("B68CF064-9299-4765-AC08-ACB49F93F892");
        private static readonly object m_timerLock = new object();
        private static readonly object m_runtimeSync = new object();
        private static readonly object m_activeSessionsLock = new object();
        private static Timer m_flushTimer;
        private static bool m_offlineQueueRuntimeAvailable = true;
        private static bool m_presenceStoreRuntimeAvailable = true;
        private static readonly Dictionary<int, ActiveSessionContext> m_activeSessions = new Dictionary<int, ActiveSessionContext>();
        private ILog m_logger = LogManager.GetLogger("DatabaseLoggerPlugin");

        public string Description
        {
            get { return "Logs various events to a configured database provider such as MySQL, MariaDB, or PostgreSQL."; }
        }

        public string Name
        {
            get { return "Database Logger"; }
        }

        public Guid Uuid
        {
            get { return PluginUuid; }
        }

        public string Version
        {
            get 
            { 
                return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(); 
            }
        }

        public void Configure()
        {
            Configuration dlg = new Configuration();
            dlg.ShowDialog();
        }

        public void SessionChange(System.ServiceProcess.SessionChangeDescription changeDescription, OpenCredential.Shared.Types.SessionProperties properties)
        {
            m_logger.DebugFormat("SessionChange({0}) - ID: {1}", changeDescription.Reason.ToString(), changeDescription.SessionId);

            lock (m_runtimeSync)
            {
                SessionIdentityCache.UpdateFromProperties(changeDescription.SessionId, properties, Settings.GetUseModifiedName());
                TryFlushOfflineQueue();
                TryLogMode(LoggerMode.SESSION, Settings.GetSessionMode(), changeDescription, properties);
                TryLogMode(LoggerMode.EVENT, Settings.GetEventMode(), changeDescription, properties);
                UpdateActiveSessions(changeDescription, properties);

                //Close the connection if it's still open
                LoggerModeFactory.closeConnection();
            }
        }

        public void Starting()
        {
            m_offlineQueueRuntimeAvailable = Settings.IsOfflineQueueEnabled();
            m_presenceStoreRuntimeAvailable = Settings.GetSessionMode() && Settings.IsPresenceTrackingEnabled();

            if (m_offlineQueueRuntimeAvailable)
            {
                try
                {
                    OfflineLogQueue.Initialize();
                }
                catch (Exception ex)
                {
                    m_offlineQueueRuntimeAvailable = false;
                    m_logger.ErrorFormat("Disabling offline SQLite queue at runtime: {0}", ex);
                }
            }

            lock (m_activeSessionsLock)
            {
                m_activeSessions.Clear();
            }
            SessionIdentityCache.Clear();

            if (m_presenceStoreRuntimeAvailable)
            {
                try
                {
                    SessionPresenceStore.Initialize();
                    RestorePersistedSessions();
                }
                catch (Exception ex)
                {
                    m_presenceStoreRuntimeAvailable = false;
                    m_logger.ErrorFormat("Disabling local session persistence at runtime: {0}", ex);
                }
            }

            StartBackgroundTasks();
        }

        public void Stopping()
        {
            StopBackgroundTasks();
            lock (m_activeSessionsLock)
            {
                m_activeSessions.Clear();
            }
            SessionIdentityCache.Clear();
        }

        private void TryLogMode(LoggerMode loggerMode, bool enabled, System.ServiceProcess.SessionChangeDescription changeDescription, SessionProperties properties)
        {
            if (!enabled)
                return;

            try
            {
                ILoggerMode mode = LoggerModeFactory.getLoggerMode(loggerMode);
                mode.Log(changeDescription, properties);
            }
            catch (Exception ex)
            {
                m_logger.WarnFormat("Failed to write {0} log to the configured database: {1}", loggerMode, ex.Message);

                if (Settings.IsOfflineQueueEnabled() && m_offlineQueueRuntimeAvailable)
                {
                    TryEnqueueOffline(loggerMode, changeDescription, properties);
                }
            }
        }

        private void StartBackgroundTasks()
        {
            lock (m_timerLock)
            {
                bool needsTimer = m_offlineQueueRuntimeAvailable || (Settings.GetSessionMode() && Settings.IsPresenceTrackingEnabled());
                if (m_flushTimer != null || !needsTimer)
                    return;

                int periodMs = Settings.GetHealthCheckSeconds() * 1000;
                m_flushTimer = new Timer(FlushOfflineQueue, null, 0, periodMs);
            }
        }

        private void StopBackgroundTasks()
        {
            lock (m_timerLock)
            {
                if (m_flushTimer != null)
                {
                    m_flushTimer.Dispose();
                    m_flushTimer = null;
                }
            }
        }

        private void FlushOfflineQueue(object state)
        {
            lock (m_runtimeSync)
            {
                TryFlushOfflineQueue();
                TrySendHeartbeats();
                LoggerModeFactory.closeConnection();
            }
        }

        private void TryFlushOfflineQueue()
        {
            if (!Settings.IsOfflineQueueEnabled() || !m_offlineQueueRuntimeAvailable)
                return;

            try
            {
                OfflineLogQueue.FlushPending();
            }
            catch (Exception ex)
            {
                m_logger.DebugFormat("Offline queue flush skipped: {0}", ex.Message);
            }
            finally
            {
                LoggerModeFactory.closeConnection();
            }
        }

        private void TryEnqueueOffline(LoggerMode loggerMode, System.ServiceProcess.SessionChangeDescription changeDescription, SessionProperties properties)
        {
            if (!m_offlineQueueRuntimeAvailable)
                return;

            try
            {
                OfflineLogQueue.Enqueue(loggerMode, changeDescription, properties);
            }
            catch (Exception ex)
            {
                m_offlineQueueRuntimeAvailable = false;
                m_logger.ErrorFormat("Disabling offline SQLite queue after runtime failure: {0}", ex);
                StopBackgroundTasks();
            }
        }

        private void UpdateActiveSessions(SessionChangeDescription changeDescription, SessionProperties properties)
        {
            if (!Settings.GetSessionMode() || !Settings.IsPresenceTrackingEnabled())
                return;

            if (properties == null)
            {
                if (changeDescription.Reason == SessionChangeReason.SessionLogoff)
                {
                    lock (m_activeSessionsLock)
                    {
                        m_activeSessions.Remove(changeDescription.SessionId);
                    }
                    SessionIdentityCache.Remove(changeDescription.SessionId);
                    RemovePersistedSessionState(changeDescription.SessionId);
                }
                return;
            }

            lock (m_activeSessionsLock)
            {
                switch (changeDescription.Reason)
                {
                    case SessionChangeReason.SessionLogon:
                    case SessionChangeReason.SessionUnlock:
                    case SessionChangeReason.ConsoleConnect:
                    case SessionChangeReason.RemoteConnect:
                        m_activeSessions[changeDescription.SessionId] = new ActiveSessionContext
                        {
                            WindowsSessionId = changeDescription.SessionId,
                            Properties = properties,
                            SessionState = "active",
                            LastHeartbeatUtc = DateTime.UtcNow
                        };
                        PersistSessionState(changeDescription.SessionId);
                        break;

                    case SessionChangeReason.SessionLock:
                        if (m_activeSessions.ContainsKey(changeDescription.SessionId))
                        {
                            m_activeSessions[changeDescription.SessionId].Properties = properties;
                            m_activeSessions[changeDescription.SessionId].SessionState = "locked";
                            m_activeSessions[changeDescription.SessionId].LastHeartbeatUtc = DateTime.UtcNow;
                        }
                        else
                        {
                            m_activeSessions[changeDescription.SessionId] = new ActiveSessionContext
                            {
                                WindowsSessionId = changeDescription.SessionId,
                                Properties = properties,
                                SessionState = "locked",
                                LastHeartbeatUtc = DateTime.UtcNow
                            };
                        }
                        PersistSessionState(changeDescription.SessionId);
                        break;

                    case SessionChangeReason.ConsoleDisconnect:
                    case SessionChangeReason.RemoteDisconnect:
                        if (m_activeSessions.ContainsKey(changeDescription.SessionId))
                        {
                            m_activeSessions[changeDescription.SessionId].Properties = properties;
                            m_activeSessions[changeDescription.SessionId].SessionState = "disconnected";
                            m_activeSessions[changeDescription.SessionId].LastHeartbeatUtc = DateTime.UtcNow;
                        }
                        PersistSessionState(changeDescription.SessionId);
                        break;

                    case SessionChangeReason.SessionLogoff:
                        m_activeSessions.Remove(changeDescription.SessionId);
                        SessionIdentityCache.Remove(changeDescription.SessionId);
                        RemovePersistedSessionState(changeDescription.SessionId);
                        break;
                }
            }
        }

        private void TrySendHeartbeats()
        {
            if (!Settings.GetSessionMode() || !Settings.IsPresenceTrackingEnabled())
                return;

            List<ActiveSessionContext> dueHeartbeats;
            DateTime nowUtc = DateTime.UtcNow;
            int heartbeatIntervalSeconds = Settings.GetHeartbeatIntervalSeconds();

            lock (m_activeSessionsLock)
            {
                dueHeartbeats = m_activeSessions.Values
                    .Where(ctx => (nowUtc - ctx.LastHeartbeatUtc).TotalSeconds >= heartbeatIntervalSeconds)
                    .Select(ctx => new ActiveSessionContext
                    {
                        WindowsSessionId = ctx.WindowsSessionId,
                        Properties = ctx.Properties,
                        SessionState = ctx.SessionState,
                        LastHeartbeatUtc = ctx.LastHeartbeatUtc
                    })
                    .ToList();
            }

            if (dueHeartbeats.Count == 0)
                return;

            SessionLogger logger = LoggerModeFactory.GetSessionLogger();
            foreach (ActiveSessionContext session in dueHeartbeats)
            {
                if (!IsRuntimeSessionAlive(session))
                {
                    ExpireActiveSession(session.WindowsSessionId, "heartbeat_timeout", nowUtc);
                    continue;
                }

                try
                {
                    logger.WriteHeartbeat(session.WindowsSessionId, session.Properties, session.SessionState, nowUtc);
                    lock (m_activeSessionsLock)
                    {
                        if (m_activeSessions.ContainsKey(session.WindowsSessionId))
                        {
                            m_activeSessions[session.WindowsSessionId].LastHeartbeatUtc = nowUtc;
                            PersistSessionState(session.WindowsSessionId);
                        }
                    }
                }
                catch (Exception ex)
                {
                    m_logger.DebugFormat("Heartbeat update skipped for session {0}: {1}", session.WindowsSessionId, ex.Message);

                    if (Settings.IsOfflineQueueEnabled() && m_offlineQueueRuntimeAvailable)
                    {
                        TryEnqueueOfflineHeartbeat(session.WindowsSessionId, session.Properties, session.SessionState, nowUtc);
                        lock (m_activeSessionsLock)
                        {
                            if (m_activeSessions.ContainsKey(session.WindowsSessionId))
                            {
                                m_activeSessions[session.WindowsSessionId].LastHeartbeatUtc = nowUtc;
                                PersistSessionState(session.WindowsSessionId);
                            }
                        }
                    }
                }
            }
        }

        private void RestorePersistedSessions()
        {
            if (!m_presenceStoreRuntimeAvailable)
                return;

            List<SessionPresenceState> persistedStates = SessionPresenceStore.LoadAll();
            if (persistedStates.Count == 0)
                return;

            lock (m_activeSessionsLock)
            {
                foreach (SessionPresenceState persistedState in persistedStates)
                {
                    if (IsLeaseExpired(persistedState.LastHeartbeatUtc, DateTime.UtcNow))
                    {
                        TryReconcileRecoveredSessionEnd(persistedState, DateTime.UtcNow, "heartbeat_timeout");
                        RemovePersistedSessionState(persistedState.WindowsSessionId);
                        continue;
                    }

                    if (!IsPersistedSessionAlive(persistedState))
                    {
                        TryReconcileRecoveredSessionEnd(persistedState, DateTime.UtcNow, "unexpected_shutdown");
                        RemovePersistedSessionState(persistedState.WindowsSessionId);
                        continue;
                    }

                    m_activeSessions[persistedState.WindowsSessionId] = new ActiveSessionContext
                    {
                        WindowsSessionId = persistedState.WindowsSessionId,
                        Properties = null,
                        SessionState = string.IsNullOrWhiteSpace(persistedState.SessionState) ? "active" : persistedState.SessionState,
                        LastHeartbeatUtc = persistedState.LastHeartbeatUtc
                    };

                    SessionIdentityCache.RememberUsername(persistedState.WindowsSessionId, persistedState.Username);
                }
            }
        }

        private bool IsLeaseExpired(DateTime lastHeartbeatUtc, DateTime nowUtc)
        {
            return (nowUtc - lastHeartbeatUtc).TotalSeconds > Settings.GetPresenceLeaseTimeoutSeconds();
        }

        private bool IsRuntimeSessionAlive(ActiveSessionContext session)
        {
            try
            {
                string liveUsername = pInvokes.GetUserName(session.WindowsSessionId);
                if (string.IsNullOrWhiteSpace(liveUsername))
                    return false;

                string expectedUsername = SessionIdentityCache.ResolveUsername(
                    session.WindowsSessionId,
                    session.Properties,
                    Settings.GetUseModifiedName(),
                    null);

                if (!string.IsNullOrWhiteSpace(expectedUsername) &&
                    !string.Equals(liveUsername, expectedUsername, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                m_logger.DebugFormat("Session {0} no longer looks alive: {1}", session.WindowsSessionId, ex.Message);
                return false;
            }
        }

        private void ExpireActiveSession(int windowsSessionId, string endReason, DateTime eventUtc)
        {
            SessionPresenceState persistedState = null;

            lock (m_activeSessionsLock)
            {
                if (m_activeSessions.ContainsKey(windowsSessionId))
                {
                    ActiveSessionContext context = m_activeSessions[windowsSessionId];
                    persistedState = new SessionPresenceState
                    {
                        WindowsSessionId = context.WindowsSessionId,
                        Username = SessionIdentityCache.ResolveUsername(
                            context.WindowsSessionId,
                            context.Properties,
                            Settings.GetUseModifiedName(),
                            null),
                        ClientSessionId = context.Properties == null || context.Properties.Id == Guid.Empty
                            ? null
                            : context.Properties.Id.ToString("D"),
                        Machine = Environment.MachineName,
                        IpAddress = GetCurrentIpAddress(),
                        SessionState = context.SessionState,
                        LastHeartbeatUtc = context.LastHeartbeatUtc
                    };
                }

                m_activeSessions.Remove(windowsSessionId);
            }

            if (persistedState != null)
                TryReconcileRecoveredSessionEnd(persistedState, eventUtc, endReason);

            SessionIdentityCache.Remove(windowsSessionId);
            RemovePersistedSessionState(windowsSessionId);
        }

        private bool IsPersistedSessionAlive(SessionPresenceState persistedState)
        {
            try
            {
                string liveUsername = pInvokes.GetUserName(persistedState.WindowsSessionId);
                if (string.IsNullOrWhiteSpace(liveUsername))
                    return false;

                if (!string.IsNullOrWhiteSpace(persistedState.Username) &&
                    !string.Equals(liveUsername, persistedState.Username, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                m_logger.DebugFormat("Discarding persisted session {0}: {1}", persistedState.WindowsSessionId, ex.Message);
                return false;
            }
        }

        private void PersistSessionState(int windowsSessionId)
        {
            if (!m_presenceStoreRuntimeAvailable || !m_activeSessions.ContainsKey(windowsSessionId))
                return;

            try
            {
                ActiveSessionContext context = m_activeSessions[windowsSessionId];
                SessionPresenceStore.Upsert(new SessionPresenceState
                {
                    WindowsSessionId = context.WindowsSessionId,
                    Username = SessionIdentityCache.ResolveUsername(
                        context.WindowsSessionId,
                        context.Properties,
                        Settings.GetUseModifiedName(),
                        null),
                    ClientSessionId = context.Properties == null || context.Properties.Id == Guid.Empty
                        ? null
                        : context.Properties.Id.ToString("D"),
                    Machine = Environment.MachineName,
                    IpAddress = GetCurrentIpAddress(),
                    SessionState = context.SessionState,
                    LastHeartbeatUtc = context.LastHeartbeatUtc
                });
            }
            catch (Exception ex)
            {
                m_presenceStoreRuntimeAvailable = false;
                m_logger.ErrorFormat("Disabling local session persistence after runtime failure: {0}", ex);
            }
        }

        private void RemovePersistedSessionState(int windowsSessionId)
        {
            if (!m_presenceStoreRuntimeAvailable)
                return;

            try
            {
                SessionPresenceStore.Remove(windowsSessionId);
            }
            catch (Exception ex)
            {
                m_presenceStoreRuntimeAvailable = false;
                m_logger.ErrorFormat("Disabling local session persistence after delete failure: {0}", ex);
            }
        }

        private void TryEnqueueOfflineHeartbeat(int windowsSessionId, SessionProperties properties, string sessionState, DateTime heartbeatUtc)
        {
            if (!m_offlineQueueRuntimeAvailable)
                return;

            try
            {
                OfflineLogQueue.EnqueueHeartbeat(
                    windowsSessionId,
                    SessionIdentityCache.ResolveUsername(windowsSessionId, properties, Settings.GetUseModifiedName(), "--UNKNOWN--"),
                    sessionState,
                    heartbeatUtc);
            }
            catch (Exception ex)
            {
                m_offlineQueueRuntimeAvailable = false;
                m_logger.ErrorFormat("Disabling offline SQLite queue after heartbeat enqueue failure: {0}", ex);
                StopBackgroundTasks();
            }
        }

        private void TryReconcileRecoveredSessionEnd(SessionPresenceState persistedState, DateTime eventUtc, string endReason)
        {
            try
            {
                SessionLogger logger = LoggerModeFactory.GetSessionLogger();
                logger.ReconcileSessionEnd(persistedState, eventUtc, endReason);
            }
            catch (Exception ex)
            {
                m_logger.DebugFormat("Recovered session close skipped for {0}: {1}", persistedState.WindowsSessionId, ex.Message);

                if (Settings.IsOfflineQueueEnabled() && m_offlineQueueRuntimeAvailable)
                {
                    TryEnqueueRecoveredSessionEnd(persistedState, eventUtc, endReason);
                }
            }
            finally
            {
                LoggerModeFactory.closeConnection();
            }
        }

        private void TryEnqueueRecoveredSessionEnd(SessionPresenceState persistedState, DateTime eventUtc, string endReason)
        {
            if (!m_offlineQueueRuntimeAvailable)
                return;

            try
            {
                OfflineLogQueue.EnqueueSessionRecovery(persistedState, eventUtc, endReason);
            }
            catch (Exception ex)
            {
                m_offlineQueueRuntimeAvailable = false;
                m_logger.ErrorFormat("Disabling offline SQLite queue after recovery enqueue failure: {0}", ex);
                StopBackgroundTasks();
            }
        }

        private string GetCurrentIpAddress()
        {
            try
            {
                foreach (System.Net.IPAddress addr in System.Net.Dns.GetHostAddresses(string.Empty))
                {
                    if (addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        return addr.ToString();
                }
            }
            catch
            {
            }

            return string.Empty;
        }


    }
}

