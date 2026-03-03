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

using pGina.Shared.Interfaces;
using pGina.Shared.Types;
using MySqlConnector;

namespace pGina.Plugin.MySQLAuth
{
    public class PluginImpl : IPluginAuthentication, IPluginAuthorization, IPluginAuthenticationGateway, IPluginConfiguration
    {
        public static readonly Guid PluginUuid = new Guid("{A89DF410-53CA-4FE1-A6CA-4479B841CA19}");
        private ILog m_logger = LogManager.GetLogger("MySQLAuth");

        public BooleanResult AuthenticateUser(SessionProperties properties)
        {
            UserInformation userInfo = properties.GetTrackedSingle<UserInformation>();
            m_logger.DebugFormat("Authenticate: {0}", userInfo.Username);

            UserEntry entry = null;
            try
            {
                using (MySqlUserDataSource dataSource = new MySqlUserDataSource())
                {
                    entry = dataSource.GetUserEntry(userInfo.Username);
                }
            }
            catch (MySqlException ex)
            {
                if (ex.Number == 1042)
                    m_logger.ErrorFormat("Unable to connect to host: {0}", Settings.Store.Host);
                else
                    m_logger.ErrorFormat("MySQL error {0}: {1}", ex.Number, ex.Message);
                throw;
            }
            catch (Exception e)
            {
                m_logger.ErrorFormat("Unexpected error: {0}", e);
                throw;
            }

            if (entry != null)
            {
                m_logger.DebugFormat("Retrieved info for user {0}. Hash: {1}", entry.Name, entry.HashAlg);
                bool passwordOk = entry.VerifyPassword(userInfo.Password);
                if (passwordOk)
                {
                    m_logger.InfoFormat("Authentication successful for {0}", userInfo.Username);
                    return new BooleanResult { Success = true, Message = "Success." };
                }
                return new BooleanResult { Success = false, Message = "Invalid username or password." };
            }
            return new BooleanResult { Success = false, Message = "Invalid username or password." };
        }

        public string Description
        {
            get { return "Uses MySQL or MariaDB server as account database. Compatible with MySQL 8.x, MariaDB 10.x/11.x, Windows 10/11."; }
        }

        public string Name
        {
            get { return "MySQL/MariaDB Auth"; }
        }

        public void Starting()
        {
            m_logger.InfoFormat("MySQL Auth Plugin starting. Version: {0}", Version);
        }

        public void Stopping()
        {
            m_logger.Info("MySQL Auth Plugin stopping.");
        }

        public Guid Uuid
        {
            get { return PluginUuid; }
        }

        public string Version
        {
            get { return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(); }
        }

        public void Configure()
        {
            Configuration dialog = new Configuration();
            dialog.ShowDialog();
        }

        public BooleanResult AuthenticatedUserGateway(SessionProperties properties)
        {
            UserInformation userInfo = properties.GetTrackedSingle<UserInformation>();

            try
            {
                using (MySqlUserDataSource dataSource = new MySqlUserDataSource())
                {
                    List<GroupGatewayRule> rules = GroupRuleLoader.GetGatewayRules();
                    foreach (GroupGatewayRule rule in rules)
                    {
                        if (rule.RuleMatch(dataSource.IsMemberOfGroup(userInfo.Username, rule.Group)))
                        {
                            userInfo.Groups.Add(new GroupInformation { Name = rule.LocalGroup });
                        }
                    }
                }
            }
            catch (MySqlException e)
            {
                bool preventLogon = Settings.Store.PreventLogonOnServerError;
                if (preventLogon)
                {
                    return new BooleanResult { Success = false, Message = string.Format("Server error: {0}", e.Message) };
                }
            }
            catch (Exception e)
            {
                m_logger.ErrorFormat("Gateway error: {0}", e);
                throw;
            }

            return new BooleanResult { Success = true };
        }

        public BooleanResult AuthorizeUser(SessionProperties properties)
        {
            m_logger.Debug("MySQL Plugin Authorization");
            bool requireAuth = Settings.Store.AuthzRequireMySqlAuth;

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
                throw new Exception("No authorization rules found.");

            try
            {
                UserInformation userInfo = properties.GetTrackedSingle<UserInformation>();
                string user = userInfo.Username;

                using (MySqlUserDataSource dataSource = new MySqlUserDataSource())
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
                            return new BooleanResult
                            {
                                Success = rule.AllowOnMatch,
                                Message = string.Format("{0} via rule '{1}'", rule.AllowOnMatch ? "Allow" : "Deny", rule.ToString())
                            };
                        }
                    }
                }
                throw new Exception("Missing default authorization rule.");
            }
            catch (Exception e)
            {
                m_logger.ErrorFormat("Authorization error: {0}", e);
                throw;
            }
        }
    }
}
