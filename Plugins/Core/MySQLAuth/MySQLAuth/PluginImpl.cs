/*
        Copyright (c) 2013, pGina Team
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
using System.Threading;
using log4net;

using pGina.Shared.Interfaces;
using pGina.Shared.Types;
using MySqlConnector;

namespace pGina.Plugin.MySQLAuth
{
    /// <summary>
    /// MySQL/MariaDB Authentication Plugin for pGina.
    /// Compatible with MySQL 8.x, MariaDB 10.x/11.x, and Windows 10/11.
    /// Supports BCrypt and legacy hash algorithms.
    /// Version: 4.1.0
    /// </summary>
    public class PluginImpl : IPluginAuthentication, IPluginAuthorization, IPluginAuthenticationGateway, IPluginConfiguration
    {
        /// <summary>
        /// Unique identifier for this plugin.
        /// </summary>
        public static readonly Guid PluginUuid = new Guid("{A89DF410-53CA-4FE1-A6CA-4479B841CA19}");
        private static readonly object m_timerLock = new object();
        private static Timer m_healthTimer;
        private static bool m_mysqlAvailable = true;
        private static bool m_localCacheRuntimeAvailable = true;
        
        private ILog m_logger = LogManager.GetLogger("MySQLAuth");

        /// <summary>
        /// Authenticates a user against the MySQL/MariaDB database.
        /// Supports BCrypt and legacy hash algorithms.
        /// </summary>
        public BooleanResult AuthenticateUser(SessionProperties properties)
        {
            if (properties == null)
            {
                m_logger.Error("AuthenticateUser called without session properties.");
                return new BooleanResult { Success = false, Message = "Missing session properties." };
            }

            UserInformation userInfo = properties.GetTrackedSingle<UserInformation>();
            if (userInfo == null || string.IsNullOrWhiteSpace(userInfo.Username))
            {
                m_logger.Error("AuthenticateUser called without valid UserInformation.");
                return new BooleanResult { Success = false, Message = "Missing user information." };
            }

            m_logger.DebugFormat("Authenticate: {0}", userInfo.Username);

            try
            {
                using (IUserDataSource dataSource = UserDataSourceFactory.Create())
                {
                    UserEntry entry = dataSource.GetUserEntry(userInfo.Username);

                    if (entry != null && Settings.IsLocalCacheEnabled() && m_localCacheRuntimeAvailable)
                    {
                        TryCacheUserEntry(entry);
                    }

                    if (entry != null)
                    {
                        m_logger.DebugFormat("Retrieved info for user {0}. Hash: {1}", entry.Name, entry.HashAlg);

                        if (Settings.IsLoginLockoutEnabled() && entry.IsCurrentlyLocked())
                        {
                            string lockMessage = entry.BlockedUntilUtc.HasValue
                                ? string.Format("Account locked until {0}.", FormatLockoutDisplay(entry.BlockedUntilUtc.Value))
                                : "Account locked.";

                            m_logger.WarnFormat(
                                "Authentication denied for {0} because account is locked. BlockedUntilDisplay={1}",
                                userInfo.Username,
                                entry.BlockedUntilUtc.HasValue ? FormatLockoutDisplay(entry.BlockedUntilUtc.Value) : "null");
                            return new BooleanResult
                            {
                                Success = false,
                                Message = lockMessage
                            };
                        }

                        bool passwordOk = entry.VerifyPassword(userInfo.Password);

                        if (passwordOk)
                        {
                            if (Settings.IsLoginLockoutEnabled())
                            {
                                dataSource.ResetFailedLoginState(userInfo.Username);
                            }

                            if (Settings.IsMigrationEnabled() && entry.HashAlg != PasswordHashAlgorithm.BCRYPT)
                            {
                                TryMigrateToBCrypt(userInfo.Username, userInfo.Password, entry);
                            }

                            m_logger.InfoFormat("Authentication successful for {0}", userInfo.Username);
                            return new BooleanResult { Success = true, Message = "Success." };
                        }

                        FailedAttemptResult failedAttemptResult =
                            dataSource.RegisterFailedLoginAttempt(userInfo.Username);

                        m_logger.WarnFormat(
                            "Authentication failed for {0} - invalid password. Failed attempts: {1}",
                            userInfo.Username,
                            failedAttemptResult.FailedAttempts);

                        if (failedAttemptResult.IsLocked)
                        {
                            string lockMessage = failedAttemptResult.BlockedUntilUtc.HasValue
                                ? string.Format("Account locked until {0}.", FormatLockoutDisplay(failedAttemptResult.BlockedUntilUtc.Value))
                                : "Account locked.";

                            return new BooleanResult
                            {
                                Success = false,
                                Message = lockMessage
                            };
                        }

                        return new BooleanResult { Success = false, Message = "Invalid username or password." };
                    }
                }
            }
            catch (MySqlException ex)
            {
                if (ex.Number == 1042)
                    m_logger.ErrorFormat("Unable to connect to host: {0}", Settings.Store.Host);
                else
                    m_logger.ErrorFormat("MySQL error {0}: {1}", ex.Number, ex.Message);
                return TryOfflineAuthentication(userInfo, ex.Message);
            }
            catch (Exception e)
            {
                m_logger.ErrorFormat("Unexpected error: {0}", e);
                return TryOfflineAuthentication(userInfo, e.Message);
            }

            m_logger.WarnFormat("Authentication failed - user {0} not found", userInfo.Username);
            return new BooleanResult { Success = false, Message = "Invalid username or password." };
        }

        /// <summary>
        /// Plugin description shown in pGina UI.
        /// </summary>
        public string Description
        {
            get { return "Uses MySQL or MariaDB server as account database. Compatible with MySQL 8.x, MariaDB 10.x/11.x, Windows 10/11. Supports BCrypt, MD5, SHA256, SHA512 hash algorithms."; }
        }

        /// <summary>
        /// Plugin name shown in pGina UI.
        /// </summary>
        public string Name
        {
            get { return "MySQL/MariaDB Auth"; }
        }

        /// <summary>
        /// Called when the plugin is starting.
        /// </summary>
        public void Starting()
        {
            m_localCacheRuntimeAvailable = Settings.IsLocalCacheEnabled();

            if (m_localCacheRuntimeAvailable)
            {
                try
                {
                    LocalUserCache.Initialize();
                }
                catch (Exception ex)
                {
                    m_localCacheRuntimeAvailable = false;
                    m_logger.ErrorFormat("Disabling local SQLite cache at runtime: {0}", ex);
                }
            }

            StartBackgroundTasks();

            m_logger.InfoFormat("MySQL Auth Plugin starting. Version: {0}", Version);
            m_logger.InfoFormat("Database: {0}@{1}:{2}", 
                Settings.Store.User, 
                Settings.Store.Host, 
                Settings.GetPort());
            m_logger.InfoFormat("Table: {0}, UsernameColumn: {1}", 
                Settings.Store.Table, 
                Settings.Store.UsernameColumn);
            m_logger.InfoFormat("TLS Mode: {0}", Settings.GetSslMode());
            m_logger.InfoFormat("Enforce Active User Status: {0}, Column: {1}, ActiveValue: {2}",
                Settings.IsUserStatusValidationEnabled(),
                Settings.GetUserStatusColumn(),
                Settings.GetUserActiveValue());
            m_logger.InfoFormat("BCrypt Work Factor: {0}, Migration Enabled: {1}",
                Settings.GetBCryptWorkFactor(),
                Settings.IsMigrationEnabled());
        }

        /// <summary>
        /// Called when the plugin is stopping.
        /// </summary>
        public void Stopping()
        {
            StopBackgroundTasks();
            m_logger.Info("MySQL Auth Plugin stopping.");
        }

        /// <summary>
        /// Unique identifier for this plugin.
        /// </summary>
        public Guid Uuid
        {
            get { return PluginUuid; }
        }

        /// <summary>
        /// Plugin version.
        /// </summary>
        public string Version
        {
            get { return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(); }
        }

        /// <summary>
        /// Opens the configuration dialog.
        /// </summary>
        public void Configure()
        {
            Configuration dialog = new Configuration();
            dialog.ShowDialog();
        }

        /// <summary>
        /// Gateway method for adding groups to authenticated users.
        /// </summary>
        public BooleanResult AuthenticatedUserGateway(SessionProperties properties)
        {
            if (properties == null)
            {
                m_logger.Error("AuthenticatedUserGateway called without session properties.");
                return new BooleanResult { Success = false, Message = "Missing session properties for gateway processing." };
            }

            UserInformation userInfo = properties.GetTrackedSingle<UserInformation>();
            if (userInfo == null || string.IsNullOrWhiteSpace(userInfo.Username))
            {
                m_logger.Error("AuthenticatedUserGateway called without valid UserInformation.");
                return new BooleanResult { Success = false, Message = "Missing user information for gateway processing." };
            }

            try
            {
                using (IUserDataSource dataSource = UserDataSourceFactory.Create())
                {
                    List<GroupGatewayRule> rules = GroupRuleLoader.GetGatewayRules();
                    foreach (GroupGatewayRule rule in rules)
                    {
                        if (rule.RuleMatch(dataSource.IsMemberOfGroup(userInfo.Username, rule.Group)))
                        {
                            userInfo.Groups.Add(new GroupInformation { Name = rule.LocalGroup });
                            m_logger.DebugFormat("Added group {0} to user {1} via gateway rule", 
                                rule.LocalGroup, userInfo.Username);
                        }
                    }
                }
            }
            catch (MySqlException e)
            {
                if (Settings.IsOfflineFallbackEnabled() && Settings.AllowOfflineBypassForAuthorization())
                {
                    m_logger.WarnFormat("Gateway offline bypass enabled: {0}", e.Message);
                    return new BooleanResult { Success = true, Message = "Gateway bypassed while MySQL is unavailable." };
                }

                bool preventLogon = Settings.PreventLogonOnServerError();
                if (preventLogon)
                {
                    m_logger.ErrorFormat("Gateway error - preventing logon: {0}", e.Message);
                    return new BooleanResult { Success = false, Message = string.Format("Server error: {0}", e.Message) };
                }
                m_logger.WarnFormat("Gateway error - allowing logon: {0}", e.Message);
            }
            catch (Exception e)
            {
                if (Settings.IsOfflineFallbackEnabled() && Settings.AllowOfflineBypassForAuthorization())
                {
                    m_logger.WarnFormat("Gateway offline bypass enabled after unexpected error: {0}", e.Message);
                    return new BooleanResult { Success = true, Message = "Gateway bypassed while MySQL is unavailable." };
                }

                m_logger.ErrorFormat("Gateway error: {0}", e);
                throw;
            }

            return new BooleanResult { Success = true };
        }

        /// <summary>
        /// Authorization method for checking user permissions.
        /// </summary>
        public BooleanResult AuthorizeUser(SessionProperties properties)
        {
            m_logger.Debug("MySQL Plugin Authorization");
            
            bool requireAuth = Settings.IsAuthzRequireMySqlAuth();
            if (properties == null)
            {
                m_logger.Error("AuthorizeUser called without session properties.");
                return new BooleanResult { Success = false, Message = "Missing session properties." };
            }

            if (requireAuth)
            {
                PluginActivityInformation actInfo = properties.GetTrackedSingle<PluginActivityInformation>();
                try
                {
                    BooleanResult mySqlResult = actInfo.GetAuthenticationResult(this.Uuid);
                    if (!mySqlResult.Success)
                    {
                        return new BooleanResult { Success = false, Message = "MySQL authentication required and failed." };
                    }
                }
                catch (KeyNotFoundException)
                {
                    return new BooleanResult { Success = false, Message = "MySQL auth not executed but required." };
                }
            }

            List<GroupAuthzRule> rules = GroupRuleLoader.GetAuthzRules();
            if (rules.Count == 0)
            {
                m_logger.Error("Authorization failed because no authorization rules are configured.");
                return new BooleanResult { Success = false, Message = "No authorization rules configured." };
            }

            try
            {
                UserInformation userInfo = properties.GetTrackedSingle<UserInformation>();
                if (userInfo == null || string.IsNullOrWhiteSpace(userInfo.Username))
                {
                    m_logger.Error("AuthorizeUser called without valid UserInformation.");
                    return new BooleanResult { Success = false, Message = "Missing user information." };
                }

                string user = userInfo.Username;

                using (IUserDataSource dataSource = UserDataSourceFactory.Create())
                {
                    foreach (GroupAuthzRule rule in rules)
                    {
                        bool inGroup = false;
                        if (rule.RuleCondition != GroupRule.Condition.ALWAYS)
                        {
                            inGroup = dataSource.IsMemberOfGroup(user, rule.Group);
                        }

                        if (rule.RuleMatch(inGroup))
                        {
                            m_logger.DebugFormat("Authorization result for {0}: {1} via rule '{2}'", 
                                user, rule.AllowOnMatch ? "Allow" : "Deny", rule.ToString());
                            return new BooleanResult
                            {
                                Success = rule.AllowOnMatch,
                                Message = string.Format("{0} via rule '{1}'", rule.AllowOnMatch ? "Allow" : "Deny", rule.ToString())
                            };
                        }
                    }
                }
                m_logger.Error("Authorization failed because no default rule matched.");
                return new BooleanResult { Success = false, Message = "Missing default authorization rule." };
            }
            catch (MySqlException e)
            {
                if (Settings.IsOfflineFallbackEnabled() && Settings.AllowOfflineBypassForAuthorization())
                {
                    m_logger.WarnFormat("Authorization offline bypass enabled because MySQL is unavailable: {0}", e.Message);
                    return new BooleanResult { Success = true, Message = "Authorization bypassed while MySQL is unavailable." };
                }

                m_logger.ErrorFormat("Authorization MySQL error: {0}", e);
                return new BooleanResult { Success = false, Message = string.Format("Authorization failed: {0}", e.Message) };
            }
            catch (Exception e)
            {
                m_logger.ErrorFormat("Authorization error: {0}", e);
                return new BooleanResult { Success = false, Message = string.Format("Authorization failed: {0}", e.Message) };
            }
        }

        private BooleanResult TryOfflineAuthentication(UserInformation userInfo, string reason)
        {
            if (userInfo == null || string.IsNullOrWhiteSpace(userInfo.Username))
            {
                m_logger.ErrorFormat("Offline authentication unavailable because user context is missing. Reason: {0}", reason);
                return new BooleanResult { Success = false, Message = "Missing user information." };
            }

            if (!Settings.IsLocalCacheEnabled() || !Settings.IsOfflineFallbackEnabled() || !m_localCacheRuntimeAvailable)
            {
                return new BooleanResult { Success = false, Message = "MySQL unavailable and offline fallback is disabled." };
            }

            UserEntry cachedEntry;
            bool authenticated = LocalUserCache.TryAuthenticate(userInfo.Username, userInfo.Password, out cachedEntry);

            if (authenticated)
            {
                m_logger.WarnFormat("Offline cache authentication successful for {0}. Reason: {1}", userInfo.Username, reason);
                return new BooleanResult { Success = true, Message = "Success (offline cache)." };
            }

            m_logger.WarnFormat("Offline cache authentication failed for {0}. Reason: {1}", userInfo.Username, reason);
            return new BooleanResult { Success = false, Message = "Invalid username or password." };
        }

        private void StartBackgroundTasks()
        {
            lock (m_timerLock)
            {
                if (m_healthTimer != null)
                    return;

                int periodMs = Settings.GetHealthCheckSeconds() * 1000;
                m_healthTimer = new Timer(BackgroundHealthCheck, null, 0, periodMs);
            }
        }

        private void StopBackgroundTasks()
        {
            lock (m_timerLock)
            {
                if (m_healthTimer != null)
                {
                    m_healthTimer.Dispose();
                    m_healthTimer = null;
                }
            }
        }

        private void BackgroundHealthCheck(object state)
        {
            if (!Settings.IsLocalCacheEnabled() || !m_localCacheRuntimeAvailable)
                return;

            try
            {
                using (IUserDataSource dataSource = UserDataSourceFactory.Create())
                {
                    m_mysqlAvailable = true;

                    DateTime? lastSyncUtc = LocalUserCache.GetLastSyncUtc();
                    if (!lastSyncUtc.HasValue ||
                        DateTime.UtcNow - lastSyncUtc.Value >= TimeSpan.FromMinutes(Settings.GetSyncIntervalMinutes()))
                    {
                        List<UserEntry> users = dataSource.GetAllUserEntriesForCache();
                        LocalUserCache.UpsertUsers(users);
                        LocalUserCache.SetLastSyncUtc(DateTime.UtcNow);
                        m_logger.InfoFormat("Local SQLite cache synchronized. Users cached: {0}", users.Count);
                    }
                }
            }
            catch (Exception ex)
            {
                if (m_mysqlAvailable)
                {
                    m_logger.WarnFormat("MySQL health check failed, switching to offline mode: {0}", ex.Message);
                }

                m_mysqlAvailable = false;
            }
        }

        private void TryCacheUserEntry(UserEntry entry)
        {
            if (!m_localCacheRuntimeAvailable || entry == null)
                return;

            try
            {
                LocalUserCache.UpsertUser(entry);
            }
            catch (Exception ex)
            {
                m_localCacheRuntimeAvailable = false;
                m_logger.ErrorFormat("Disabling local SQLite cache after runtime failure: {0}", ex);
                StopBackgroundTasks();
            }
        }

        /// <summary>
        /// Attempts to migrate a user's password hash to BCrypt.
        /// Called after successful authentication with a legacy hash.
        /// </summary>
        private void TryMigrateToBCrypt(string username, string password, UserEntry entry)
        {
            try
            {
                m_logger.InfoFormat("Migrating hash for user {0} from {1} to BCrypt", 
                    username, entry.HashAlg);

                // Generate new BCrypt hash
                int workFactor = Settings.GetBCryptWorkFactor();
                string newHash = BCryptHasher.HashPassword(password, workFactor);

                // Update database with new hash
                using (IUserDataSource dataSource = UserDataSourceFactory.Create())
                {
                    dataSource.UpdateUserHash(username, newHash, "BCRYPT");
                }

                m_logger.InfoFormat("Hash migration completed for user {0}", username);
            }
            catch (Exception ex)
            {
                // Log warning but don't fail authentication
                m_logger.WarnFormat("Failed to migrate hash for user {0}: {1}", username, ex.Message);
            }
        }

        private static string FormatLockoutDisplay(DateTime blockedUntilUtc)
        {
            DateTime localTime = blockedUntilUtc.Kind == DateTimeKind.Utc
                ? blockedUntilUtc.ToLocalTime()
                : blockedUntilUtc;

            return localTime.ToString("yyyy-MM-dd HH:mm:ss");
        }
    }
}
