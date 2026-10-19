using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using Steamworks;

namespace SteamCloudFileManager
{
    class RemoteStorage : IRemoteStorage, IDisposable
    {
        static RemoteStorage instance;
        static object sync = new object();

        internal bool IsDisposed { get; private set; }
        internal uint AppId { get; private set; }

        public bool IsCloudEnabledForAccount
        {
            get
            {
                //checkDisposed();
                // Not static because we need to ensure Steamworks API is initted
                return SteamRemoteStorage.IsCloudEnabledForAccount();
            }
        }
        public bool IsCloudEnabledForApp
        {
            get
            {
                checkDisposed();
                return SteamRemoteStorage.IsCloudEnabledForApp();
            }
            set
            {
                checkDisposed();
                SteamRemoteStorage.SetCloudEnabledForApp(value);
            }
        }

        internal RemoteStorage(uint appID)
        {
            AppId = appID;
            Environment.SetEnvironmentVariable("SteamAppID", appID.ToString());
            bool init = SteamAPI.Init();
            string failureHint = null;
            if (!init)
            {
                // Setting environment variable didn't work, so use steam_appid.txt instead.
                // The file is only needed while SteamAPI.Init() runs, so always remove it again,
                // even if something fails along the way.
                bool wroteAppIdFile = false;
                failureHint = "SteamAPI.Init() failed with SteamAppID environment variable as well as steam_appid.txt.";
                try
                {
                    File.WriteAllText("steam_appid.txt", appID.ToString());
                    wroteAppIdFile = true;
                }
                catch (Exception ex)
                {
                    failureHint = "Could not write steam_appid.txt into the working directory ("
                        + Path.GetFullPath(".") + "): " + ex.Message
                        + ". Make sure the directory is writable and not read-only.";
                }
                finally
                {
                    if (wroteAppIdFile)
                    {
                        try { File.Delete("steam_appid.txt"); }
                        catch { /* best effort */ }
                    }
                }

                if (wroteAppIdFile)
                {
                    init = SteamAPI.Init();
                }
            }

            if (!init)
            {
                throw new Exception(
                    "Cannot initialize Steamworks API for App ID " + appID + ". " + failureHint + Environment.NewLine +
                    "Check the following:" + Environment.NewLine +
                    "- Steam is running and you are logged in." + Environment.NewLine +
                    "- The App ID is correct (check it on steamdb.info)." + Environment.NewLine +
                    "- The account has access to this app (e.g. it is in your library)." + Environment.NewLine +
                    "- steam_api.dll is next to the exe and matches the architecture (32/64 bit).");
            }
        }

        public List<IRemoteFile> GetFiles()
        {
            checkDisposed();
            List<IRemoteFile> files = new List<IRemoteFile>();

            int fileCount = SteamRemoteStorage.GetFileCount();
            for (int i = 0; i < fileCount; ++i)
            {
                int length;
                string name = SteamRemoteStorage.GetFileNameAndSize(i, out length);
                if (string.IsNullOrEmpty(name)) continue; // skip malformed entries instead of creating files with null names
                RemoteFile file = new RemoteFile(this, name);
                files.Add(file);
            }

            return files;
        }

        public IRemoteFile GetFile(string name)
        {
            checkDisposed();
            return new RemoteFile(this, name.ToLowerInvariant());
        }

        public bool GetQuota(out int totalBytes, out int availableBytes)
        {
            checkDisposed();
            return SteamRemoteStorage.GetQuota(out totalBytes, out availableBytes);
        }

        void checkDisposed()
        {
            if (IsDisposed) throw new InvalidOperationException("Instance is no longer valid.");
        }

        public void Dispose()
        {
            if (!IsDisposed)
            {
                SteamAPI.Shutdown();
                IsDisposed = true;
            }
        }

        public static RemoteStorage CreateInstance(uint appID)
        {
            lock (sync)
            {
                if (instance != null)
                {
                    instance.Dispose();
                    instance = null;
                }

                RemoteStorage rs = new RemoteStorage(appID);
                instance = rs;
                return rs;
            }
        }
    }
}
