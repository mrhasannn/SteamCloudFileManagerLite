Steam Cloud File Manager Lite
=============================

This program is a lite version of the Steam Cloud File Manager. It allow you to list files stored in Steam Cloud, and to download, upload, back up and delete them.

To begin, enter the App ID of the game you're interested in manipulating the files of, and click Connect. After the file list has loaded, you can see the files and their properties (name, timestamp, size, persisted/exists state, sync platforms), along with how much cloud space you have used.

Features
--------
* List Steam Cloud files of any app together with their properties
* Download a file to a location of your choice
* Upload a local file back into a game's Steam Cloud (restore saves)
  - asks before overwriting and keeps a local backup of the current cloud version first
* Back up *all* listed files to a folder with one click (folder structure is preserved)
* Delete one or multiple files, with a confirmation listing what will be deleted
* Steamworks callbacks are pumped continuously, so the list appears without a manual Refresh dance

Note files that reside directly on the filesystem (Auto-Cloud, e.g. some game saves) do not appear in the list. To modify those files and sync them back to Steam Cloud, launch the game, modify (or delete) the files you want, and close the game. Your modified files will be synced back, and those deleted will also be deleted on Steam Cloud.

You may find [Steam DB](https://steamdb.info/) useful in finding App IDs. The User Cloud tab on Steam DB's app pages is also helpful for finding where synced save files are.

Requirements
------------
* Windows (build and runtime) - the app runs as a 32 bit process because Steamworks.NET 5.0.0 ships the 32 bit steam_api.dll
* Steam client running and logged in
* The target game's App ID; the account generally needs access to that app

Building
--------
1. Open `SteamCloudFileManager.sln` in Visual Studio (2017 or later recommended; target framework .NET Framework 4.8).
2. Build. `libs\Steamworks.NET.dll` is referenced from a relative path inside the repository, so no machine specific setup is needed.
3. The build copies `libs\Steamworks.NET.dll`, `libs\CSteamworks.dll` and `libs\steam_api.dll` next to the exe automatically (MSBuild `CopySteamRedist` target). All three must stay beside the exe, otherwise the app fails to load (`CSteamworks` / `steam_api.dll` not found).

Alternatively build `SteamCloudFileManager.sln` with MSBuild or `msbuild /p:Configuration=Release` from a Developer Command Prompt.

Command line options
--------------------
* `/appid <n>` - connect to the given App ID on startup, e.g. `SteamCloudFileManager.exe /appid 440`

Notes
-----
* You can change the App ID and click Connect again at any time; the tool shuts down the previous Steamworks session before starting a new one.
* If connecting fails, the error message includes the most common causes (Steam not running, wrong App ID, no access to the app, steam_api.dll missing).
* An offline test backend (`RemoteStorageLocal`, a fake storage backed by a folder) is included for development, it is not used by the UI.

Included third party libraries
------------------------------
* [Steamworks.NET](https://github.com/rlabrecque/Steamworks.NET) 5.0.0 (MIT license, see `libs\Steamworks.NET-LICENSE.txt`), plus Valve's native steam_api.dll redistributable.
* Based on [GMMan/SteamCloudFileManagerLite](https://github.com/GMMan/SteamCloudFileManagerLite).
