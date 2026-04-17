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
        public static readonly Guid PluginUuid = new Guid("B68CF064-9299-4765-AC08-ACB49F93F892");
        private static readonly object m_timerLock = new object();
        private static Timer m_flushTimer;
        private static bool m_offlineQueueRuntimeAvailable = true;
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

            TryFlushOfflineQueue();
            TryLogMode(LoggerMode.SESSION, Settings.GetSessionMode(), changeDescription, properties);
            TryLogMode(LoggerMode.EVENT, Settings.GetEventMode(), changeDescription, properties);

            //Close the connection if it's still open
            LoggerModeFactory.closeConnection();
            
        }

        public void Starting()
        {
            m_offlineQueueRuntimeAvailable = Settings.IsOfflineQueueEnabled();

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

            StartBackgroundTasks();
        }

        public void Stopping()
        {
            StopBackgroundTasks();
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
                if (m_flushTimer != null || !m_offlineQueueRuntimeAvailable)
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
            TryFlushOfflineQueue();
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


    }
}

