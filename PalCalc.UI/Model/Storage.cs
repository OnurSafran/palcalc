using Newtonsoft.Json;
using PalCalc.Model;
using PalCalc.SaveReader;
using PalCalc.UI.Persistence;
using PalCalc.UI.Persistence.Serialization;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PalCalc.UI.Model
{
    internal sealed class SaveCustomizationsLoadResult
    {
        public SaveCustomizationsLoadResult(
            SaveCustomizations data,
            bool canPersist,
            string error = null)
        {
            Data = data ?? new SaveCustomizations();
            CanPersist = canPersist;
            Error = error;
        }

        public SaveCustomizations Data { get; }
        public bool CanPersist { get; }
        public string Error { get; }
    }

    internal static class Storage
    {
        private static ILogger logger = Log.ForContext(typeof(Storage));

        public static event Action<ISaveGame> SaveReloaded;
        public static event Action<ISavesLocation, ISaveGame, CachedSaveGame> SaveReloadedWithCache;
        public static event Action<ISaveGame> SaveRemoved;

        // (debug-only setting)
        public static readonly bool DEBUG_DisableStorage = false;

        public static string CachePath => "cache";
        public static string SaveCachePath => $"{CachePath}/saves";
        public static string DataPath => "data";

        public static string AppSettingsPath
        {
            get
            {
                Init();
                return Path.Combine(DataPath, "settings.json");
            }
        }

        // path for cached copy of save file data
        public static string SaveCachePathFor(ISaveGame forSaveFile)
        {
            Init();
            return Path.Combine(SaveCachePath, $"{CachedSaveGame.IdentifierFor(forSaveFile)}.json");
        }

        // path for storing data associated with a specific save file
        public static string SaveFileDataPath(ISaveGame forSaveFile)
        {
            Init();
            var path = Path.Combine(DataPath, CachedSaveGame.IdentifierFor(forSaveFile));
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            return path;
        }

        public static string SaveFileTargetsDataPath(ISaveGame forSaveFile) => Path.Join(SaveFileDataPath(forSaveFile), "targets");

        // path for storing game-specific game settings (breeding time, etc.)
        public static string GameSettingsPath(ISaveGame forSaveFile)
        {
            Init();
            return Path.Combine(SaveFileDataPath(forSaveFile), "game-settings.json");
        }

        public static string CustomContainerPath(ISaveGame forSaveFile)
        {
            Init();
            return Path.Combine(SaveFileDataPath(forSaveFile), "custom-containers.json");
        }

        // Your Pals is a separate save-owned document.
        public static string YourPalsDocumentPath(ISaveGame forSaveFile)
        {
            Init();
            return Path.Combine(SaveFileDataPath(forSaveFile), YourPalsContract.DocumentFileName);
        }

        private static bool didInit = false;
        public static void Init()
        {
            if (didInit) return;

            if (!Directory.Exists(CachePath)) Directory.CreateDirectory(CachePath);
            if (!Directory.Exists(SaveCachePath)) Directory.CreateDirectory(SaveCachePath);
            if (!Directory.Exists(DataPath)) Directory.CreateDirectory(DataPath);

            StorageMigrationRunner.EnsureCurrent(DataPath);

            didInit = true;
        }

        public static AppSettings LoadAppSettings()
        {
            if (DEBUG_DisableStorage) return new();

            if (File.Exists(AppSettingsPath))
            {
                try
                {
                    var res = AppSettingsJsonSerializer.FromDto(
                        AppSettingsJsonSerializer.FromCurrentJson(File.ReadAllText(AppSettingsPath))
                    );

                    // remove duplicates caused by missing `ObjectCreationHandling` in older versions
                    res.SolverSettings.BannedBredPalInternalNames = res.SolverSettings.BannedBredPalInternalNames.Distinct().ToList();
                    res.SolverSettings.BannedWildPalInternalNames = res.SolverSettings.BannedWildPalInternalNames.Distinct().ToList();

                    return res;
                }
                catch (Exception e)
                {
                    logger.Error(e, "error reading app settings files, keeping the file and using defaults");

                    // Leave malformed authoritative data in place so it can be recovered manually.
                    return new();
                }
            }
            else
            {
                return new();
            }
        }

        public static void SaveAppSettings(AppSettings settings)
        {
            if (DEBUG_DisableStorage) return;

            Init();
            var dto = AppSettingsJsonSerializer.ToDto(settings);
            var json = AppSettingsJsonSerializer.ToJson(dto);
            StorageFile.WriteAtomic(AppSettingsPath, json, backup: true);
        }

        public static bool TrySaveAppSettings(AppSettings settings)
        {
            try
            {
                SaveAppSettings(settings);
                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "error writing app settings");
                return false;
            }
        }

        public static void ClearForSave(ISaveGame save)
        {
            try
            {
                var cachePath = SaveCachePathFor(save);
                if (File.Exists(cachePath)) File.Delete(cachePath);
            }
            catch (Exception ex)
            {
                logger.Warning(ex, "Unable to delete cache-file for {saveId}", save.GameId);
            }

            try
            {
                var dataPath = SaveFileDataPath(save);
                if (Directory.Exists(dataPath))
                {
                    foreach (var file in Directory.EnumerateFiles(dataPath))
                    {
                        var fileName = Path.GetFileName(file);
                        if (string.Equals(fileName, YourPalsContract.DocumentFileName, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(fileName, YourPalsContract.DocumentFileName + ".bak", StringComparison.OrdinalIgnoreCase))
                            continue;

                        File.Delete(file);
                    }

                    foreach (var directory in Directory.EnumerateDirectories(dataPath))
                        Directory.Delete(directory, true);

                    // SaveFileDataPath creates the folder on access, so a removed
                    // save would otherwise always leave an empty directory behind.
                    // Retained Your Pals documents keep the folder non-empty.
                    if (!Directory.EnumerateFileSystemEntries(dataPath).Any())
                        Directory.Delete(dataPath);
                }
            }
            catch (Exception ex)
            {
                logger.Warning(ex, "Unable to delete data-folder for {saveId}", save.GameId);
            }
        }

        public static SaveCustomizationsLoadResult LoadSaveCustomizations(ISaveGame forSaveGame, PalDB db)
        {
            if (DEBUG_DisableStorage)
                return new SaveCustomizationsLoadResult(new SaveCustomizations(), canPersist: true);

            var filePath = CustomContainerPath(forSaveGame);
            if (!File.Exists(filePath))
                return new SaveCustomizationsLoadResult(new SaveCustomizations(), canPersist: true);

            Exception loadError = null;
            SaveCustomizations res = PCDebug.HandleErrors(
                action: () => CustomizationsJsonSerializer.ToRuntime(
                    CustomizationsJsonSerializer.FromCurrentJson(File.ReadAllText(filePath)),
                    db
                ),
                handleErr: (re) =>
                {
                    loadError = re;
                    logger.Warning(re, "failed to load save customizations for {label}; keeping the file and disabling autosave", CachedSaveGame.IdentifierFor(forSaveGame));
                    return null;
                }
            );

            if (loadError != null)
            {
                return new SaveCustomizationsLoadResult(
                    new SaveCustomizations(),
                    canPersist: false,
                    loadError.Message);
            }

            res ??= new SaveCustomizations();
            res.CustomContainers ??= [];
            return new SaveCustomizationsLoadResult(res, canPersist: true);
        }

        public static void SaveCustomizations(ISaveGame forSaveGame, SaveCustomizations custom, PalDB db)
        {
            if (DEBUG_DisableStorage) return;

            var json = CustomizationsJsonSerializer.ToJson(CustomizationsJsonSerializer.ToDto(custom));
            StorageFile.WriteAtomic(CustomContainerPath(forSaveGame), json, backup: true);
        }

        #region Cached Game Save Files

        private static Dictionary<string, CachedSaveGame> InMemorySaves = new Dictionary<string, CachedSaveGame>();

        // only loads the save if it has been cached, otherwise returns null
        public static CachedSaveGame LoadSaveFromCache(ISaveGame save, PalDB db)
        {
            Init();

            CrashSupport.ReferencedSave(save);

            if (DEBUG_DisableStorage) return null;

            var path = SaveCachePathFor(save);
            if (File.Exists(path))
            {
                CachedSaveGame res = PCDebug.HandleErrors(
                    action: () =>
                    {
                        var csg = CachedSaveGame.FromJson(File.ReadAllText(path), db);
                        csg.UnderlyingSave = save;
                        return csg;
                    },
                    handleErr: (ex) =>
                    {
                        logger.Error(ex, "failed to load cached save-game data, clearing");

                        File.Delete(path);
                        return null;
                    }
                );

                CrashSupport.ReferencedCachedSave(res);
                return res;
            }
            else
            {
                return null;
            }
        }

        // loads the cached save data and updates it if it's outdated or not yet cached
        public static CachedSaveGame LoadSave(ISavesLocation containerLocation, ISaveGame save, PalDB db, GameSettings settings)
        {
            Init();

            CrashSupport.ReferencedSave(save);

            var path = SaveCachePathFor(save);
            if (!save.IsValid)
            {
                if (!DEBUG_DisableStorage && File.Exists(path))
                {
                    logger.Warning("cached save available but the save-game itself is invalid, deleting cached save for {savePath}", save.BasePath);
                    File.Delete(path);
                }
                return null;
            }

            var identifier = CachedSaveGame.IdentifierFor(save);

            lock (InMemorySaves)
            {
                if (InMemorySaves.ContainsKey(identifier)) return InMemorySaves[identifier];

                if (!DEBUG_DisableStorage && File.Exists(path))
                {
                    var res = LoadSaveFromCache(save, db);

                    // A malformed cache is deleted by LoadSaveFromCache. Fall through to a
                    // fresh save-file load, which owns the normal load lifecycle events.
                    if (res == null)
                        return LoadSave(containerLocation, save, db, settings);

                    if (!res.IsValid)
                    {
                        // TODO - no longer necessary? should have been covered by check at top of this method
                        // TODO - log
                        File.Delete(path);
                        return null;
                    }

                    if (res.IsOutdated(db))
                    {
                        File.Delete(path);
                        return LoadSave(containerLocation, save, db, settings);
                    }

                    InMemorySaves.Add(identifier, res);
                    return res;
                }
                else
                {
                    var res = CachedSaveGame.FromSaveGame(containerLocation, save, db, settings);
                    if (res != null)
                    {
                        CrashSupport.ReferencedCachedSave(res);

                        if (!DEBUG_DisableStorage)
                            File.WriteAllText(path, res.ToJson(db));
                    }

                    // Keep failed loads as null entries. SaveGameViewModel.CachedValue is used
                    // by several components, and negative caching prevents each access from
                    // retrying the load and raising another error notification. ReloadSave
                    // explicitly removes this entry before retrying.
                    if (InMemorySaves.ContainsKey(identifier))
                        InMemorySaves.Remove(identifier);

                    InMemorySaves.Add(identifier, res);
                    return res;
                }
            }
        }

        // Removes all data related to the save (in memory + on disk), but does _not_ remove
        // any related entries within AppSettings
        public static void RemoveSave(ISaveGame save)
        {
            lock (InMemorySaves)
                InMemorySaves.Remove(CachedSaveGame.IdentifierFor(save));

            CrashSupport.RemoveReferences(save);
            ClearForSave(save);
            SaveRemoved?.Invoke(save);
        }

        public static void ReloadSave(ISavesLocation containerLocation, ISaveGame save, PalDB db, GameSettings settings)
        {
            Init();

            if (save == null) return;

            CrashSupport.ReferencedSave(save);

            CachedSaveGame refreshedCachedSave = null;

            lock (InMemorySaves)
            {
                var identifier = CachedSaveGame.IdentifierFor(save);
                var originalCachedSave = InMemorySaves.GetValueOrDefault(identifier);

                if (originalCachedSave != null)
                    CrashSupport.ReferencedCachedSave(originalCachedSave);

                // Remove successful and negatively cached results alike so an explicit reload
                // always performs a new load attempt.
                InMemorySaves.Remove(identifier);

                var path = SaveCachePathFor(save);
                var wasStored = !DEBUG_DisableStorage && File.Exists(path);
                var backupPath = wasStored ? path + ".bak" : null;

                if (wasStored)
                {
                    if (File.Exists(backupPath)) File.Delete(backupPath);
                    File.Move(path, backupPath);
                }

                var newCachedSave = LoadSave(containerLocation, save, db, settings);

                if (newCachedSave == null)
                {
                    if (!DEBUG_DisableStorage && wasStored)
                    {
                        if (File.Exists(path)) File.Delete(path);

                        File.Move(backupPath, path);
                    }

                    InMemorySaves[identifier] = originalCachedSave;
                }
                else
                {
                    if (!DEBUG_DisableStorage)
                    {
                        if (wasStored) File.Delete(backupPath);

                        if (originalCachedSave != null)
                            originalCachedSave.CopyFrom(newCachedSave);
                    }

                    InMemorySaves[identifier] = originalCachedSave ?? newCachedSave;
                    refreshedCachedSave = InMemorySaves[identifier];

                    SaveReloaded?.Invoke(save);
                }
            }

            if (refreshedCachedSave != null)
                SaveReloadedWithCache?.Invoke(containerLocation, save, refreshedCachedSave);
        }

        #endregion
    }
}
