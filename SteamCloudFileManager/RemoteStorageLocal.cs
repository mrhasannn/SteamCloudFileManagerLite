using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace SteamCloudFileManager
{
    /// <summary>
    /// Dummy local implementation of RemoteStorage interface for offline testing
    /// </summary>
    class RemoteStorageLocal : IRemoteStorage
    {
        string basePath;
        uint appId;
        
        public RemoteStorageLocal(string basePath, uint appId)
        {
            this.basePath = basePath;
            this.appId = appId;
            Directory.CreateDirectory(Path.Combine(basePath, appId.ToString()));
        }

        public IRemoteFile GetFile(string name)
        {
            string lowerName = name.ToLowerInvariant();
            return new RemoteFileLocal(lowerName, new FileInfo(Path.Combine(basePath, appId.ToString(), lowerName)));
        }

        public List<IRemoteFile> GetFiles()
        {
            string searchPath = Path.Combine(basePath, appId.ToString());
            List<IRemoteFile> files = new List<IRemoteFile>();
            foreach (string file in Directory.GetFiles(searchPath, "*", SearchOption.AllDirectories))
            {
                // Substring instead of Replace: Replace would replace *every* occurrence of searchPath
                // and is case sensitive, both of which can corrupt the relative path.
                string curPath = file.Substring(searchPath.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .ToLowerInvariant()
                    .Replace(Path.DirectorySeparatorChar, '/') // Steam cloud paths always use '/'
                    ;
                files.Add(new RemoteFileLocal(curPath, new FileInfo(file)));
            }
            return files;
        }

        public bool GetQuota(out int totalBytes, out int availableBytes)
        {
            string root = Path.GetPathRoot(Path.GetFullPath(basePath));
            if (string.IsNullOrEmpty(root)) throw new IOException("Cannot determine drive for " + basePath + ".");
            DriveInfo di = new DriveInfo(root);
            totalBytes = di.TotalSize > int.MaxValue ? int.MaxValue : (int)di.TotalSize;
            availableBytes = di.AvailableFreeSpace > int.MaxValue ? int.MaxValue : (int)di.AvailableFreeSpace;
            return true;
        }

        public bool IsCloudEnabledForAccount
        {
            get { return true; }
        }

        public bool IsCloudEnabledForApp
        {
            get
            {
                return true;
            }
            set
            {
                throw new NotSupportedException();
            }
        }
    }
}
