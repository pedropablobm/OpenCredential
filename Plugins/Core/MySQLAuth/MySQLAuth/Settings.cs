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
using System.IO;
using System.Reflection;

namespace pGina.Plugin.MySQLAuth
{
    /// <summary>
    /// Settings management class for MySQL Authentication plugin.
    /// Contains all configuration options including security settings.
    /// Version: 4.1.0 - Fixed RuntimeBinderException issues
    /// </summary>
    public class Settings
    {
        /// <summary>
        /// Encoding format for password hashes in the database.
        /// </summary>
        public enum HashEncoding { HEX = 0, BASE_64 = 1 };

        private static dynamic m_settings = new pGina.Shared.Settings.pGinaDynamicSettings(PluginImpl.PluginUuid);
        
        /// <summary>
        /// Dynamic settings store for accessing configuration values.
        /// </summary>
        public static dynamic Store
        {
            get { return m_settings; }
        }

        static Settings()
        {
            // =============================================
            // Database Connection Settings
            // =============================================
            m_settings.SetDefault("Host", "localhost");
            m_settings.SetDefault("Port", 3306);
            m_settings.SetDefault("UseSsl", false);
            m_settings.SetDefault("SslMode", MySqlConnector.MySqlSslMode.None.ToString());
            m_settings.SetDefault("User", "pgina_user");
            m_settings.SetDefaultEncryptedSetting("Password", "secret");
            m_settings.SetDefault("Database", "bdcontrolasistenciasalas");

            // Connection timeout settings (for MySQL 8/MariaDB)
            m_settings.SetDefault("ConnectionTimeout", 30);
            m_settings.SetDefault("CommandTimeout", 30);

            // =============================================
            // User Table Configuration
            // =============================================
            m_settings.SetDefault("Table", "estudiantes");
            m_settings.SetDefault("HashEncoding", (int)HashEncoding.HEX);
            m_settings.SetDefault("UsernameColumn", "codigo");
            m_settings.SetDefault("HashMethodColumn", "metodo_hash");
            m_settings.SetDefault("PasswordColumn", "clave");
            m_settings.SetDefault("UserTablePrimaryKeyColumn", "id");
            m_settings.SetDefault("EnforceUserStatus", true);
            m_settings.SetDefault("UserStatusColumn", "estado");
            m_settings.SetDefault("UserActiveValue", "1");
            m_settings.SetDefault("EnableLoginLockout", true);
            m_settings.SetDefault("FailedAttemptsColumn", "intentos_fallidos");
            m_settings.SetDefault("BlockedUntilColumn", "bloqueado_hasta");
            m_settings.SetDefault("LastAttemptColumn", "ultimo_intento");
            m_settings.SetDefault("MaxFailedAttempts", 5);
            m_settings.SetDefault("LockoutMinutes", 15);

            // =============================================
            // Group Table Configuration
            // =============================================
            m_settings.SetDefault("GroupTableName", "groups");
            m_settings.SetDefault("GroupNameColumn", "group_name");
            m_settings.SetDefault("GroupTablePrimaryKeyColumn", "group_id");

            // User-Group relationship table
            m_settings.SetDefault("UserGroupTableName", "usergroup");
            m_settings.SetDefault("UserForeignKeyColumn", "user_id");
            m_settings.SetDefault("GroupForeignKeyColumn", "group_id");

            // =============================================
            // Authorization Settings
            // =============================================
            m_settings.SetDefault("GroupAuthzRules", new string[] { (new GroupAuthzRule(true)).ToRegString() });
            m_settings.SetDefault("AuthzRequireMySqlAuth", false);

            // Gateway settings
            m_settings.SetDefault("GroupGatewayRules", new string[] { });
            m_settings.SetDefault("PreventLogonOnServerError", false);

            // =============================================
            // BCrypt Security Settings
            // =============================================
            m_settings.SetDefault("BCryptWorkFactor", 10);
            m_settings.SetDefault("MigrateToBCrypt", false);

            // Offline resilience
            m_settings.SetDefault("LocalCacheEnabled", true);
            m_settings.SetDefault("OfflineFallbackEnabled", true);
            m_settings.SetDefault("AllowOfflineBypassForAuthorization", true);
            m_settings.SetDefault("SyncIntervalMinutes", 5);
            m_settings.SetDefault("HealthCheckSeconds", 30);
            m_settings.SetDefault("LocalCachePath", string.Empty);
        }

        // =============================================
        // Helper Methods - Use these to avoid RuntimeBinderException
        // =============================================

        private static object UnwrapSettingValue(object value)
        {
            if (value == null)
                return null;

            PropertyInfo rawValueProperty = value.GetType().GetProperty("RawValue");
            return rawValueProperty != null
                ? rawValueProperty.GetValue(value, null)
                : value;
        }

        private static string GetStringSetting(string name, string defaultValue = "")
        {
            object value = UnwrapSettingValue(m_settings.GetSetting(name, defaultValue));
            return value == null ? defaultValue : Convert.ToString(value);
        }

        private static int GetIntSetting(string name, int defaultValue = 0)
        {
            object value = UnwrapSettingValue(m_settings.GetSetting(name, defaultValue));
            if (value == null)
                return defaultValue;

            if (value is int intValue)
                return intValue;

            if (value is long longValue)
                return Convert.ToInt32(longValue);

            if (value is string stringValue && int.TryParse(stringValue, out int parsed))
                return parsed;

            return Convert.ToInt32(value);
        }

        private static bool GetBoolSetting(string name, bool defaultValue = false)
        {
            object value = UnwrapSettingValue(m_settings.GetSetting(name, defaultValue));
            if (value == null)
                return defaultValue;

            if (value is bool boolValue)
                return boolValue;

            if (value is int intValue)
                return intValue != 0;

            if (value is string stringValue)
            {
                if (bool.TryParse(stringValue, out bool parsedBool))
                    return parsedBool;

                if (int.TryParse(stringValue, out int parsedInt))
                    return parsedInt != 0;
            }

            return Convert.ToBoolean(value);
        }

        /// <summary>
        /// Gets the configured BCrypt work factor, ensuring it's within valid range (4-12).
        /// </summary>
        public static int GetBCryptWorkFactor()
        {
            int workFactor = GetIntSetting("BCryptWorkFactor", 10);
            if (workFactor < 4 || workFactor > 12)
            {
                return 10; // Default safe value
            }
            return workFactor;
        }

        /// <summary>
        /// Checks if BCrypt hash migration is enabled.
        /// </summary>
        public static bool IsMigrationEnabled()
        {
            return GetBoolSetting("MigrateToBCrypt");
        }
        
        /// <summary>
        /// Gets the connection timeout setting.
        /// </summary>
        public static int GetConnectionTimeout()
        {
            return GetIntSetting("ConnectionTimeout", 30);
        }
        
        /// <summary>
        /// Gets the command timeout setting.
        /// </summary>
        public static int GetCommandTimeout()
        {
            return GetIntSetting("CommandTimeout", 30);
        }
        
        /// <summary>
        /// Gets the database port setting.
        /// </summary>
        public static int GetPort()
        {
            return GetIntSetting("Port", 3306);
        }
        
        /// <summary>
        /// Gets the hash encoding setting.
        /// </summary>
        public static HashEncoding GetHashEncoding()
        {
            return (HashEncoding)GetIntSetting("HashEncoding", (int)HashEncoding.HEX);
        }
        
        /// <summary>
        /// Checks if SSL is enabled for database connection.
        /// </summary>
        public static bool IsSslEnabled()
        {
            return GetBoolSetting("UseSsl");
        }

        /// <summary>
        /// Gets the configured MySQL TLS mode, preserving compatibility with the legacy UseSsl flag.
        /// </summary>
        public static MySqlConnector.MySqlSslMode GetSslMode()
        {
            string configuredMode = GetStringSetting("SslMode");
            MySqlConnector.MySqlSslMode parsedMode;

            if (!string.IsNullOrEmpty(configuredMode) &&
                Enum.TryParse(configuredMode, true, out parsedMode))
            {
                return parsedMode;
            }

            return IsSslEnabled()
                ? MySqlConnector.MySqlSslMode.Required
                : MySqlConnector.MySqlSslMode.None;
        }
        
        /// <summary>
        /// Checks if authorization requires MySQL auth.
        /// </summary>
        public static bool IsAuthzRequireMySqlAuth()
        {
            return GetBoolSetting("AuthzRequireMySqlAuth");
        }
        
        /// <summary>
        /// Checks if logon should be prevented on server error.
        /// </summary>
        public static bool PreventLogonOnServerError()
        {
            return GetBoolSetting("PreventLogonOnServerError");
        }

        /// <summary>
        /// Checks if user status validation is enabled.
        /// </summary>
        public static bool IsUserStatusValidationEnabled()
        {
            return GetBoolSetting("EnforceUserStatus", true);
        }

        /// <summary>
        /// Gets the configured user status column.
        /// </summary>
        public static string GetUserStatusColumn()
        {
            return GetStringSetting("UserStatusColumn");
        }

        /// <summary>
        /// Gets the configured active value for the user status column.
        /// </summary>
        public static string GetUserActiveValue()
        {
            return GetStringSetting("UserActiveValue");
        }

        public static bool IsLocalCacheEnabled()
        {
            return GetBoolSetting("LocalCacheEnabled", true);
        }

        public static bool IsLoginLockoutEnabled()
        {
            return GetBoolSetting("EnableLoginLockout", true);
        }

        public static string GetFailedAttemptsColumn()
        {
            return GetStringSetting("FailedAttemptsColumn");
        }

        public static string GetBlockedUntilColumn()
        {
            return GetStringSetting("BlockedUntilColumn");
        }

        public static string GetLastAttemptColumn()
        {
            return GetStringSetting("LastAttemptColumn");
        }

        public static int GetMaxFailedAttempts()
        {
            return Math.Max(1, GetIntSetting("MaxFailedAttempts", 5));
        }

        public static int GetLockoutMinutes()
        {
            return Math.Max(1, GetIntSetting("LockoutMinutes", 15));
        }

        public static bool IsOfflineFallbackEnabled()
        {
            return GetBoolSetting("OfflineFallbackEnabled", true);
        }

        public static bool AllowOfflineBypassForAuthorization()
        {
            return GetBoolSetting("AllowOfflineBypassForAuthorization", true);
        }

        public static int GetSyncIntervalMinutes()
        {
            return Math.Max(1, GetIntSetting("SyncIntervalMinutes", 5));
        }

        public static int GetHealthCheckSeconds()
        {
            return Math.Max(5, GetIntSetting("HealthCheckSeconds", 30));
        }

        public static string GetLocalCachePath()
        {
            string configuredPath = GetStringSetting("LocalCachePath");
            if (!string.IsNullOrWhiteSpace(configuredPath))
                return configuredPath;

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "pGina",
                "MySQLAuth",
                "mysqlauth-cache.sqlite");
        }
    }
}
