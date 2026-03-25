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
using System.Net;
using System.ServiceProcess;

using log4net;
using MySqlConnector;
using pGina.Shared.Types;

namespace pGina.Plugin.MySqlLogger
{
    class SessionLogger : ILoggerMode
    {
        private ILog m_logger = LogManager.GetLogger("MySqlLoggerPlugin");
        private MySqlConnection m_conn;

        public SessionLogger() { }

        public bool Log(SessionChangeDescription changeDescription, SessionProperties properties)
        {
            if (m_conn == null)
                throw new InvalidOperationException("No MySQL Connection present.");

            string username = "--UNKNOWN--";
            if (properties != null)
            {
                UserInformation ui = properties.GetTrackedSingle<UserInformation>();
                if (ui != null)
                    username = ResolveUsername(ui);
            }

            if (changeDescription.Reason == SessionChangeReason.SessionLogon)
            {
                if (m_conn.State != System.Data.ConnectionState.Open) m_conn.Open();

                string table = Settings.Store.SessionTable;
                string updatesql = string.Format("UPDATE `{0}` SET logoutstamp=NOW() WHERE logoutstamp IS NULL AND username=@username AND machine=@machine AND ipaddress=@ipaddress", table);

                using (var cmd = new MySqlCommand(updatesql, m_conn))
                {
                    cmd.Parameters.AddWithValue("@username", username);
                    cmd.Parameters.AddWithValue("@machine", Environment.MachineName);
                    cmd.Parameters.AddWithValue("@ipaddress", getIPAddress());
                    cmd.ExecuteNonQuery();
                }

                string insertsql = string.Format("INSERT INTO `{0}` (dbid, loginstamp, logoutstamp, username, machine, ipaddress) VALUES (NULL, NOW(), NULL, @username, @machine, @ipaddress)", table);
                using (var cmd = new MySqlCommand(insertsql, m_conn))
                {
                    cmd.Parameters.AddWithValue("@username", username);
                    cmd.Parameters.AddWithValue("@machine", Environment.MachineName);
                    cmd.Parameters.AddWithValue("@ipaddress", getIPAddress());
                    cmd.ExecuteNonQuery();
                }

                m_logger.DebugFormat("Logged LogOn event for {0}", username);
            }
            else if (changeDescription.Reason == SessionChangeReason.SessionLogoff)
            {
                if (m_conn.State != System.Data.ConnectionState.Open) m_conn.Open();

                string table = Settings.Store.SessionTable;
                string updatesql = string.Format("UPDATE `{0}` SET logoutstamp=NOW() WHERE logoutstamp IS NULL AND username=@username AND machine=@machine AND ipaddress=@ipaddress", table);

                using (var cmd = new MySqlCommand(updatesql, m_conn))
                {
                    cmd.Parameters.AddWithValue("@username", username);
                    cmd.Parameters.AddWithValue("@machine", Environment.MachineName);
                    cmd.Parameters.AddWithValue("@ipaddress", getIPAddress());
                    cmd.ExecuteNonQuery();
                }

                m_logger.DebugFormat("Logged LogOff event for {0}", username);
            }

            return true;
        }

        public string TestTable()
        {
            if (m_conn == null) throw new InvalidOperationException("No MySQL Connection present.");

            try
            {
                if (m_conn.State != System.Data.ConnectionState.Open) m_conn.Open();

                string table = Settings.Store.SessionTable;
                bool tableExists = false;

                using (var cmd = new MySqlCommand("SHOW TABLES", m_conn))
                using (var rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read())
                        if (Convert.ToString(rdr[0]) == table) tableExists = true;
                }

                if (!tableExists)
                    return "Connection successful, but table does not exist. Click 'Create Table'.";

                string[] columns = { "dbid", "loginstamp", "logoutstamp", "username", "machine", "ipaddress" };
                using (var cmd = new MySqlCommand("DESCRIBE `" + table + "`", m_conn))
                using (var rdr = cmd.ExecuteReader())
                {
                    int colCt = 0;
                    while (rdr.Read())
                    {
                        if (!columns.Contains(Convert.ToString(rdr[0])))
                            return "Table exists but has invalid columns.";
                        colCt++;
                    }
                    return colCt == columns.Length ? "Table exists and is correct." : "Table has incorrect columns.";
                }
            }
            catch (MySqlException ex)
            {
                return string.Format("Error: {0}", ex.Message);
            }
        }

        public string CreateTable()
        {
            if (m_conn == null) throw new InvalidOperationException("No MySQL Connection present.");

            try
            {
                if (m_conn.State != System.Data.ConnectionState.Open) m_conn.Open();

                string table = Settings.Store.SessionTable;
                string sql = string.Format(
                    "CREATE TABLE `{0}` (dbid BIGINT NOT NULL AUTO_INCREMENT PRIMARY KEY, loginstamp DATETIME NOT NULL, " +
                    "logoutstamp DATETIME NULL, username VARCHAR(128) NOT NULL, machine VARCHAR(128) NOT NULL, " +
                    "ipaddress VARCHAR(45) NOT NULL, INDEX idx_{0}_active (logoutstamp, machine, ipaddress), " +
                    "INDEX idx_{0}_user (username, machine, ipaddress)) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4", table);

                using (var cmd = new MySqlCommand(sql, m_conn))
                {
                    cmd.ExecuteNonQuery();
                }
                return "Table created.";
            }
            catch (MySqlException ex)
            {
                return string.Format("Error: {0}", ex.Message);
            }
        }

        public void SetConnection(MySqlConnection conn) { this.m_conn = conn; }

        private string ResolveUsername(UserInformation userInfo)
        {
            if (userInfo == null)
                return "--UNKNOWN--";

            string username = Settings.GetUseModifiedName() ? userInfo.Username : userInfo.OriginalUsername;
            return string.IsNullOrWhiteSpace(username) ? "--UNKNOWN--" : username;
        }

        private string getIPAddress()
        {
            foreach (IPAddress addr in Dns.GetHostAddresses(""))
                if (addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    return addr.ToString();
            return "-INVALID IP ADDRESS-";
        }
    }
}
