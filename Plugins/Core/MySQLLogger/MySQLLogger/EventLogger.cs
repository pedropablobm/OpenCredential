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

using log4net;
using MySqlConnector;
using pGina.Shared.Types;

namespace pGina.Plugin.MySqlLogger
{
    class EventLogger : ILoggerMode
    {
        private ILog m_logger = LogManager.GetLogger("MySqlLoggerPlugin");
        public static readonly string UNKNOWN_USERNAME = "--Unknown--";
        private MySqlConnection m_conn;

        public EventLogger() { }

        public bool Log(System.ServiceProcess.SessionChangeDescription changeDescription, SessionProperties properties)
        {
            string msg = null;
            switch (changeDescription.Reason)
            {
                case System.ServiceProcess.SessionChangeReason.SessionLogon:
                    msg = LogonEvent(changeDescription.SessionId, properties);
                    break;
                case System.ServiceProcess.SessionChangeReason.SessionLogoff:
                    msg = LogoffEvent(changeDescription.SessionId, properties);
                    break;
                case System.ServiceProcess.SessionChangeReason.SessionLock:
                    msg = SessionLockEvent(changeDescription.SessionId, properties);
                    break;
                case System.ServiceProcess.SessionChangeReason.SessionUnlock:
                    msg = SessionUnlockEvent(changeDescription.SessionId, properties);
                    break;
                case System.ServiceProcess.SessionChangeReason.SessionRemoteControl:
                    msg = RemoteControlEvent(changeDescription.SessionId, properties);
                    break;
                case System.ServiceProcess.SessionChangeReason.ConsoleConnect:
                    msg = ConsoleConnectEvent(changeDescription.SessionId, properties);
                    break;
                case System.ServiceProcess.SessionChangeReason.ConsoleDisconnect:
                    msg = ConsoleDisconnectEvent(changeDescription.SessionId, properties);
                    break;
                case System.ServiceProcess.SessionChangeReason.RemoteConnect:
                    msg = RemoteConnectEvent(changeDescription.SessionId, properties);
                    break;
                case System.ServiceProcess.SessionChangeReason.RemoteDisconnect:
                    msg = RemoteDisconnectEvent(changeDescription.SessionId, properties);
                    break;
            }

            if (!string.IsNullOrEmpty(msg))
            {
                if (m_conn == null) throw new InvalidOperationException("No MySQL Connection present.");
                logToServer(msg);
            }
            return true;
        }

        public string TestTable()
        {
            if (m_conn == null) throw new InvalidOperationException("No MySQL Connection present.");

            try
            {
                if (m_conn.State != System.Data.ConnectionState.Open) m_conn.Open();

                string table = Settings.Store.EventTable;
                bool tableExists = false;

                using (var cmd = new MySqlCommand("SHOW TABLES", m_conn))
                using (var rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read())
                        if (Convert.ToString(rdr[0]) == table) tableExists = true;
                }

                if (!tableExists)
                    return "Connection successful, but table does not exist. Click 'Create Table'.";

                string[] columns = { "TimeStamp", "Host", "Ip", "Machine", "Message" };
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
                return string.Format("Connection failed: {0}", ex.Message);
            }
        }

        public string CreateTable()
        {
            if (m_conn == null) throw new InvalidOperationException("No MySQL Connection present.");

            try
            {
                if (m_conn.State != System.Data.ConnectionState.Open) m_conn.Open();

                string table = Settings.Store.EventTable;
                string sql = string.Format(
                    "CREATE TABLE `{0}` (TimeStamp DATETIME, Host TINYTEXT, Ip VARCHAR(45), " +
                    "Machine TINYTEXT, Message TEXT) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4", table);

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

        private bool logToServer(string message)
        {
            if (m_conn.State != System.Data.ConnectionState.Open) m_conn.Open();

            string table = Settings.Store.EventTable;
            string sql = string.Format("INSERT INTO `{0}`(TimeStamp, Host, Ip, Machine, Message) VALUES (NOW(), @host, @ip, @machine, @message)", table);

            using (var cmd = new MySqlCommand(sql, m_conn))
            {
                cmd.Parameters.AddWithValue("@host", Dns.GetHostName());
                cmd.Parameters.AddWithValue("@ip", getIPAddress());
                cmd.Parameters.AddWithValue("@machine", Environment.MachineName);
                cmd.Parameters.Add("@message", MySqlDbType.Text);
                cmd.Parameters["@message"].Value = message;
                cmd.ExecuteNonQuery();
            }
            return true;
        }

        private string getIPAddress()
        {
            foreach (IPAddress addr in Dns.GetHostAddresses(""))
                if (addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    return addr.ToString();
            return "";
        }

        private string getUsername(SessionProperties properties)
        {
            if (properties == null) return UNKNOWN_USERNAME;
            UserInformation userInfo = properties.GetTrackedSingle<UserInformation>();
            if (userInfo == null) return UNKNOWN_USERNAME;
            return Settings.GetUseModifiedName() ? userInfo.Username : userInfo.OriginalUsername;
        }

        private string LogonEvent(int sessionId, SessionProperties properties)
        {
            return Settings.GetEvtLogon() ? string.Format("[{0}] Logon user: {1}", sessionId, getUsername(properties) ?? UNKNOWN_USERNAME) : "";
        }

        private string LogoffEvent(int sessionId, SessionProperties properties)
        {
            return Settings.GetEvtLogoff() ? string.Format("[{0}] Logoff user: {1}", sessionId, getUsername(properties) ?? UNKNOWN_USERNAME) : "";
        }

        private string ConsoleConnectEvent(int sessionId, SessionProperties properties)
        {
            return Settings.GetEvtConsoleConnect() ? string.Format("[{0}] Console connect", sessionId) : "";
        }

        private string ConsoleDisconnectEvent(int sessionId, SessionProperties properties)
        {
            return Settings.GetEvtConsoleDisconnect() ? string.Format("[{0}] Console disconnect", sessionId) : "";
        }

        private string RemoteDisconnectEvent(int sessionId, SessionProperties properties)
        {
            return Settings.GetEvtRemoteDisconnect() ? string.Format("[{0}] Remote disconnect user: {1}", sessionId, getUsername(properties) ?? UNKNOWN_USERNAME) : "";
        }

        private string RemoteConnectEvent(int sessionId, SessionProperties properties)
        {
            return Settings.GetEvtRemoteConnect() ? string.Format("[{0}] Remote connect user: {1}", sessionId, getUsername(properties) ?? UNKNOWN_USERNAME) : "";
        }

        private string RemoteControlEvent(int sessionId, SessionProperties properties)
        {
            return Settings.GetEvtRemoteControl() ? string.Format("[{0}] Remote control user: {1}", sessionId, getUsername(properties) ?? UNKNOWN_USERNAME) : "";
        }

        private string SessionUnlockEvent(int sessionId, SessionProperties properties)
        {
            return Settings.GetEvtUnlock() ? string.Format("[{0}] Session unlock user: {1}", sessionId, getUsername(properties) ?? UNKNOWN_USERNAME) : "";
        }

        private string SessionLockEvent(int sessionId, SessionProperties properties)
        {
            return Settings.GetEvtLock() ? string.Format("[{0}] Session lock user: {1}", sessionId, getUsername(properties) ?? UNKNOWN_USERNAME) : "";
        }
    }
}
