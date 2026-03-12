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
using System.Data;
using System.Linq;
using System.Text;

using log4net;

// Updated to use MySqlConnector for MySQL 8.x and MariaDB compatibility
using MySqlConnector;

namespace pGina.Plugin.MySQLAuth
{
    /// <summary>
    /// Provides access to user data stored in MySQL/MariaDB database.
    /// Supports MySQL 8.x and MariaDB 10.x/11.x.
    /// </summary>
    class MySqlUserDataSource : IDisposable
    {
        private MySqlConnection m_conn = null;
        private ILog m_logger;
        private bool m_disposed = false;

        public MySqlUserDataSource()
        {
            m_logger = LogManager.GetLogger("MySqlUserDataSource");

            try
            {
                var builder = new MySqlConnectionStringBuilder();
                builder.Server = Settings.Store.Host;
                
                // FIX: Use helper method with proper casting
                int port = Settings.GetPort();
                builder.Port = Convert.ToUInt32(port > 0 ? port : 3306);
                
                builder.UserID = Settings.Store.User;
                builder.Database = Settings.Store.Database;
                builder.Password = Settings.Store.GetEncryptedSetting("Password");

                // SSL/TLS Configuration - FIX: Use helper method
                builder.SslMode = Settings.GetSslMode();

                // MySQL 8.x and MariaDB compatibility settings
                builder.AllowUserVariables = true;
                builder.AllowZeroDateTime = true;
                builder.ConvertZeroDateTime = true;
                
                // Timeout settings - FIX: Use helper methods
                builder.ConnectionTimeout = (uint)Settings.GetConnectionTimeout();
                builder.DefaultCommandTimeout = (uint)Settings.GetCommandTimeout();
                
                // Character set and pooling
                builder.CharacterSet = "utf8mb4";
                builder.Pooling = true;

                m_conn = new MySqlConnection(builder.ConnectionString);
                m_conn.Open();
                
                m_logger.DebugFormat("Connected to MySQL. Server version: {0}", m_conn.ServerVersion);
            }
            catch (MySqlException ex)
            {
                m_logger.ErrorFormat("MySQL connection error: {0}", ex.Message);
                throw;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!m_disposed)
            {
                if (disposing && m_conn != null)
                {
                    try
                    {
                        m_conn.Close();
                        m_conn.Dispose();
                    }
                    catch (Exception ex)
                    {
                        m_logger.WarnFormat("Error disposing connection: {0}", ex.Message);
                    }
                    m_conn = null;
                }
                m_disposed = true;
            }
        }

        /// <summary>
        /// Gets user entry from the database including password hash information.
        /// Supports BCrypt and legacy hash algorithms.
        /// </summary>
        public UserEntry GetUserEntry(string userName)
        {
            if (m_conn == null || m_conn.State != ConnectionState.Open)
            {
                m_logger.Error("Database connection is not open");
                return null;
            }

            bool enforceUserStatus = Settings.IsUserStatusValidationEnabled();
            string query = enforceUserStatus
                ? string.Format("SELECT `{1}`, `{2}`, `{3}`, `{4}` FROM `{0}` WHERE `{1}` = @user",
                    Settings.Store.Table,
                    Settings.Store.UsernameColumn,
                    Settings.Store.HashMethodColumn,
                    Settings.Store.PasswordColumn,
                    Settings.GetUserStatusColumn())
                : string.Format("SELECT `{1}`, `{2}`, `{3}` FROM `{0}` WHERE `{1}` = @user",
                    Settings.Store.Table,
                    Settings.Store.UsernameColumn,
                    Settings.Store.HashMethodColumn,
                    Settings.Store.PasswordColumn);

            m_logger.DebugFormat("Executing query to find user: {0}", userName);

            using (var cmd = new MySqlCommand(query, m_conn))
            {
                cmd.Parameters.AddWithValue("@user", userName);

                using (var rdr = cmd.ExecuteReader())
                {
                    if (rdr.HasRows)
                    {
                        rdr.Read();

                        string uname = rdr[0].ToString();
                        string hashMethodStr = rdr[1] != null && rdr[1] != DBNull.Value
                            ? rdr[1].ToString().ToUpper().Trim()
                            : "NONE";
                        string hash = rdr[2] != null && rdr[2] != DBNull.Value
                            ? rdr[2].ToString()
                            : "";
                        string statusValue = enforceUserStatus && rdr.FieldCount > 3 && rdr[3] != DBNull.Value
                            ? rdr[3].ToString().Trim()
                            : string.Empty;

                        PasswordHashAlgorithm hashAlg;

                        m_logger.DebugFormat("User {0} found, hash method from DB: {1}", uname, hashMethodStr);

                        if (enforceUserStatus &&
                            !string.Equals(statusValue, Settings.GetUserActiveValue(), StringComparison.OrdinalIgnoreCase))
                        {
                            m_logger.WarnFormat(
                                "User {0} rejected because status '{1}' does not match active value '{2}'",
                                uname,
                                statusValue,
                                Settings.GetUserActiveValue());
                            return null;
                        }

                        switch (hashMethodStr)
                        {
                            case "NONE":
                                hashAlg = PasswordHashAlgorithm.NONE;
                                break;
                            case "MD5":
                                hashAlg = PasswordHashAlgorithm.MD5;
                                break;
                            case "SMD5":
                                hashAlg = PasswordHashAlgorithm.SMD5;
                                break;
                            case "SHA1":
                                hashAlg = PasswordHashAlgorithm.SHA1;
                                break;
                            case "SSHA1":
                                hashAlg = PasswordHashAlgorithm.SSHA1;
                                break;
                            case "SHA256":
                                hashAlg = PasswordHashAlgorithm.SHA256;
                                break;
                            case "SSHA256":
                                hashAlg = PasswordHashAlgorithm.SSHA256;
                                break;
                            case "SHA384":
                                hashAlg = PasswordHashAlgorithm.SHA384;
                                break;
                            case "SSHA384":
                                hashAlg = PasswordHashAlgorithm.SSHA384;
                                break;
                            case "SHA512":
                                hashAlg = PasswordHashAlgorithm.SHA512;
                                break;
                            case "SSHA512":
                                hashAlg = PasswordHashAlgorithm.SSHA512;
                                break;
                            case "BCRYPT":
                            case "BCRYPT_SHA256":
                                hashAlg = PasswordHashAlgorithm.BCRYPT;
                                m_logger.DebugFormat("User {0} using BCrypt hash", uname);
                                break;
                            default:
                                // Auto-detect BCrypt by hash format
                                if (hash.Length == 60 &&
                                    (hash.StartsWith("$2a$") || hash.StartsWith("$2b$") || hash.StartsWith("$2y$")))
                                {
                                    m_logger.WarnFormat("Hash method column says '{0}' but hash appears to be BCrypt for user {1}",
                                        hashMethodStr, uname);
                                    hashAlg = PasswordHashAlgorithm.BCRYPT;
                                }
                                else
                                {
                                    m_logger.ErrorFormat("Unrecognized hash method: {0} for user {1}",
                                        hashMethodStr, uname);
                                    return null;
                                }
                                break;
                        }

                        m_logger.DebugFormat("Retrieved user entry for {0}, hash algorithm: {1}", uname, hashAlg);
                        return new UserEntry(uname, hashAlg, hash);
                    }

                    m_logger.DebugFormat("User {0} not found in database", userName);
                    return null;
                }
            }
        }

        /// <summary>
        /// Checks if a user is a member of a specific group.
        /// </summary>
        public bool IsMemberOfGroup(string userName, string groupName)
        {
            if (m_conn == null || m_conn.State != ConnectionState.Open)
            {
                m_logger.Error("Database connection is not open");
                return false;
            }

            // Get user ID first
            string query = string.Format("SELECT `{2}` FROM `{0}` WHERE `{1}` = @user",
                Settings.Store.Table,
                Settings.Store.UsernameColumn,
                Settings.Store.UserTablePrimaryKeyColumn);

            string userId = null;

            using (var cmd = new MySqlCommand(query, m_conn))
            {
                cmd.Parameters.AddWithValue("@user", userName);

                using (var rdr = cmd.ExecuteReader())
                {
                    if (rdr.HasRows)
                    {
                        rdr.Read();
                        userId = rdr[0].ToString();
                    }
                }
            }

            if (userId == null)
            {
                m_logger.DebugFormat("User {0} not found when checking group membership", userName);
                return false;
            }

            // Check group membership
            query = string.Format(
                "SELECT `{0}`.`{5}` FROM `{0}`, `{1}` " +
                "WHERE `{0}`.`{4}` = `{1}`.`{3}` AND `{1}`.`{2}` = @userId",
                Settings.Store.GroupTableName,
                Settings.Store.UserGroupTableName,
                Settings.Store.UserForeignKeyColumn,
                Settings.Store.GroupForeignKeyColumn,
                Settings.Store.GroupTablePrimaryKeyColumn,
                Settings.Store.GroupNameColumn);

            using (var cmd = new MySqlCommand(query, m_conn))
            {
                cmd.Parameters.AddWithValue("@userId", userId);

                using (var rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read())
                    {
                        if (rdr[0].ToString().Equals(groupName, StringComparison.OrdinalIgnoreCase))
                        {
                            m_logger.DebugFormat("User {0} IS member of group {1}", userName, groupName);
                            return true;
                        }
                    }
                }
            }

            m_logger.DebugFormat("User {0} is NOT member of group {1}", userName, groupName);
            return false;
        }

        /// <summary>
        /// Updates a user's password hash in the database.
        /// Used for BCrypt migration feature.
        /// </summary>
        /// <param name="userName">Username to update</param>
        /// <param name="newHash">New password hash</param>
        /// <param name="hashMethod">Hash method name (e.g., "BCRYPT")</param>
        public void UpdateUserHash(string userName, string newHash, string hashMethod)
        {
            if (m_conn == null || m_conn.State != ConnectionState.Open)
            {
                m_logger.Error("Database connection is not open");
                throw new InvalidOperationException("Database connection is not open");
            }

            string query = string.Format("UPDATE `{0}` SET `{2}` = @hashMethod, `{3}` = @hash WHERE `{1}` = @user",
                Settings.Store.Table,
                Settings.Store.UsernameColumn,
                Settings.Store.HashMethodColumn,
                Settings.Store.PasswordColumn);

            m_logger.DebugFormat("Updating hash for user: {0} to {1}", userName, hashMethod);

            using (var cmd = new MySqlCommand(query, m_conn))
            {
                cmd.Parameters.AddWithValue("@user", userName);
                cmd.Parameters.AddWithValue("@hashMethod", hashMethod);
                cmd.Parameters.AddWithValue("@hash", newHash);

                int rowsAffected = cmd.ExecuteNonQuery();
                
                if (rowsAffected > 0)
                {
                    m_logger.InfoFormat("Successfully updated hash for user {0}", userName);
                }
                else
                {
                    m_logger.WarnFormat("No rows affected when updating hash for user {0}", userName);
                }
            }
        }
    }
}
