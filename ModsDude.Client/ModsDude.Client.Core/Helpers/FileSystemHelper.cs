﻿namespace ModsDude.Client.Core.Helpers;
public static class FileSystemHelper
{
    public static string GetAppDataDirectory()
    {
        var localAppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        return Path.Combine(localAppDataPath, "ModsDude");
    }
}
