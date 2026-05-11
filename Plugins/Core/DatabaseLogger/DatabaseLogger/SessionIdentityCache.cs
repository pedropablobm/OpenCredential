using System.Collections.Generic;
using OpenCredential.Shared.Types;

namespace OpenCredential.Plugin.DatabaseLogger
{
    internal static class SessionIdentityCache
    {
        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<int, string> UsernamesBySessionId = new Dictionary<int, string>();

        public static void UpdateFromProperties(int windowsSessionId, SessionProperties properties, bool useModifiedUsername)
        {
            string username = ExtractUsername(properties, useModifiedUsername);
            if (string.IsNullOrWhiteSpace(username))
                return;

            lock (SyncRoot)
            {
                UsernamesBySessionId[windowsSessionId] = username;
            }
        }

        public static string ResolveUsername(int windowsSessionId, SessionProperties properties, bool useModifiedUsername, string fallback)
        {
            string username = ExtractUsername(properties, useModifiedUsername);
            if (!string.IsNullOrWhiteSpace(username))
            {
                lock (SyncRoot)
                {
                    UsernamesBySessionId[windowsSessionId] = username;
                }

                return username;
            }

            lock (SyncRoot)
            {
                if (UsernamesBySessionId.TryGetValue(windowsSessionId, out string cachedUsername) &&
                    !string.IsNullOrWhiteSpace(cachedUsername))
                {
                    return cachedUsername;
                }
            }

            return fallback;
        }

        public static void Remove(int windowsSessionId)
        {
            lock (SyncRoot)
            {
                UsernamesBySessionId.Remove(windowsSessionId);
            }
        }

        public static void Clear()
        {
            lock (SyncRoot)
            {
                UsernamesBySessionId.Clear();
            }
        }

        private static string ExtractUsername(SessionProperties properties, bool useModifiedUsername)
        {
            if (properties == null)
                return null;

            UserInformation userInfo = properties.GetTrackedSingle<UserInformation>();
            if (userInfo == null)
                return null;

            string username = useModifiedUsername ? userInfo.Username : userInfo.OriginalUsername;
            return string.IsNullOrWhiteSpace(username) ? null : username;
        }
    }
}
