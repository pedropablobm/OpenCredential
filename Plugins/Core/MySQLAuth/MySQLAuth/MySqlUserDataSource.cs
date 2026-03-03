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

using log4net;

// Updated to use MySqlConnector for MySQL 8.x and MariaDB compatibility
using MySqlConnector;

namespace pGina.Plugin.MySQLAuth
{
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
                int port = Settings.Store.Port;
                builder.Port = Convert.ToUInt32(port > 0 ? port : 3306);
                builder.UserID = Settings.Store.User;
                builder.Database = Settings.Store.Database;
                builder.Password = Settings.Store.GetEncryptedSetting("Password");

                // SSL/TLS Configuration
                bool useSsl = Settings.Store.UseSsl;
                builder.SslMode = useSsl ? MySqlSslMode.Required : MySqlSslMode.None;

                // MySQL 8.x and MariaDB compatibility
                builder.AllowUserVariables = true;
                builder.AllowZeroDateTime = true;
                builder.ConvertZeroDateTime = true;
                builder.ConnectionTimeout = 30;
                builder.DefaultCommandTimeout = 30;
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
                    m_conn.Close();
                    m_conn.Dispose();
                    m_conn = null;
                }
                m_disposed = true;
            }
        }

        public UserEntry GetUserEntry(string userName)
        {
            if (m_conn == null || m_conn.State != System.Data.ConnectionState.Open)
                return null;

            string query = string.Format("SELECT `{1}`, `{2}`, `{3}` FROM `{0}` WHERE `{1}` = @user",
                Settings.Store.Table, Settings.Store.UsernameColumn, 
                Settings.Store.HashMethodColumn, Settings.Store.PasswordColumn);

            using (var cmd = new MySqlCommand(query, m_conn))
            {
                cmd.Parameters.AddWithValue("@user", userName);
                using (var rdr = cmd.ExecuteReader())
                {
                    if (rdr.HasRows)
                    {
                        rdr.Read();
                        PasswordHashAlgorithm hashAlg;
                        string uname = rdr[0].ToString();
                        string hash = rdr[2].ToString();

                        switch (rdr[1] != null ? rdr[1].ToString().ToUpper() : "NONE")
                        {
                            case "NONE": hashAlg = PasswordHashAlgorithm.NONE; break;
                            case "MD5": hashAlg = PasswordHashAlgorithm.MD5; break;
                            case "SMD5": hashAlg = PasswordHashAlgorithm.SMD5; break;
                            case "SHA1": hashAlg = PasswordHashAlgorithm.SHA1; break;
                            case "SSHA1": hashAlg = PasswordHashAlgorithm.SSHA1; break;
                            case "SHA256": hashAlg = PasswordHashAlgorithm.SHA256; break;
                            case "SSHA256": hashAlg = PasswordHashAlgorithm.SSHA256; break;
                            case "SHA512": hashAlg = PasswordHashAlgorithm.SHA512; break;
                            case "SSHA512": hashAlg = PasswordHashAlgorithm.SSHA512; break;
                            case "SHA384": hashAlg = PasswordHashAlgorithm.SHA384; break;
                            case "SSHA384": hashAlg = PasswordHashAlgorithm.SSHA384; break;
                            default:
                                m_logger.ErrorFormat("Unrecognized hash: {0}", rdr[1]);
                                return null;
                        }
                        return new UserEntry(uname, hashAlg, hash);
                    }
                    return null;
                }
            }
        }

        public bool IsMemberOfGroup(string userName, string groupName)
        {
            if (m_conn == null || m_conn.State != System.Data.ConnectionState.Open)
                return false;

            string query = string.Format("SELECT `{1}`, `{2}` FROM `{0}` WHERE `{1}` = @user",
                Settings.Store.Table, Settings.Store.UsernameColumn, Settings.Store.UserTablePrimaryKeyColumn);

            string user_id = null;
            using (var cmd = new MySqlCommand(query, m_conn))
            {
                cmd.Parameters.AddWithValue("@user", userName);
                using (var rdr = cmd.ExecuteReader())
                {
                    if (rdr.HasRows)
                    {
                        rdr.Read();
                        user_id = rdr[1].ToString();
                    }
                }
            }

            if (user_id == null) return false;

            query = string.Format("SELECT `{0}`.`{5}` FROM `{0}`, `{1}` WHERE `{0}`.`{4}` = `{1}`.`{3}` AND `{1}`.`{2}` = @user_id",
                Settings.Store.GroupTableName, Settings.Store.UserGroupTableName, 
                Settings.Store.UserForeignKeyColumn, Settings.Store.GroupForeignKeyColumn,
                Settings.Store.GroupTablePrimaryKeyColumn, Settings.Store.GroupNameColumn);

            using (var cmd = new MySqlCommand(query, m_conn))
            {
                cmd.Parameters.AddWithValue("@user_id", user_id);
                using (var rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read())
                    {
                        if (rdr[0].ToString().Equals(groupName, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }
            return false;
        }
    }
}
