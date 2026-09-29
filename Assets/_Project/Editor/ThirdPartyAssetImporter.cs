using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Модель дрона — асет з Unity Asset Store (PBR Racing Drone, JackBurfy). Його ліцензія
/// забороняє класти файли в репозиторій, тому кожен завантажує пакет під своїм акаунтом,
/// а цей скрипт при відкритті проєкту сам знаходить його в кеші Asset Store та імпортує.
/// Пакет зберігає оригінальні GUID, тож Drone.prefab підхоплює меш і матеріали без правок.
/// </summary>
[InitializeOnLoad]
public static class ThirdPartyAssetImporter
{
    private const string PackageName = "PBR Racing Drone";
    private const string PackageFile = PackageName + ".unitypackage";
    private const string StoreUrl = "https://assetstore.unity.com/packages/3d/vehicles/air/pbr-racing-drone-59751";
    // GUID "Racing Drone v2.FBX" — на нього посилається Drone.prefab
    private const string ModelGuid = "1c7c68c6ddb11f646aef48769e84fee4";
    private const string PromptShownKey = "FlyDrone.PbrDronePromptShown";

    static ThirdPartyAssetImporter()
    {
        // delayCall — щоб не імпортувати посеред завантаження AssetDatabase
        EditorApplication.delayCall += () => EnsureImported(interactive: !Application.isBatchMode);
    }

    public static bool IsInstalled
    {
        get
        {
            var path = AssetDatabase.GUIDToAssetPath(ModelGuid);
            return !string.IsNullOrEmpty(path) && File.Exists(path);
        }
    }

    [MenuItem("Tools/FlyDrone/Import PBR Racing Drone")]
    private static void ImportFromMenu()
    {
        if (IsInstalled)
        {
            EditorUtility.DisplayDialog(PackageName, "Асет вже імпортовано.", "OK");
            return;
        }
        SessionState.SetBool(PromptShownKey, false);
        EnsureImported(interactive: true);
    }

    private static void EnsureImported(bool interactive)
    {
        if (IsInstalled) return;

        var cached = FindCachedPackage();
        if (cached != null)
        {
            Debug.Log($"[FlyDrone] Імпортую {PackageName} з кешу Asset Store: {cached}");
            AssetDatabase.ImportPackage(cached, false);
            return;
        }

        if (!interactive)
        {
            Debug.LogWarning($"[FlyDrone] {PackageName} не знайдено в кеші Asset Store — дрон буде без моделі. " +
                             $"Завантажте асет: {StoreUrl}");
            return;
        }

        // Питаємо один раз за сесію редактора, щоб не набридати після кожної рекомпіляції
        if (SessionState.GetBool(PromptShownKey, false)) return;
        SessionState.SetBool(PromptShownKey, true);

        int choice = EditorUtility.DisplayDialogComplex(
            PackageName,
            "Проєкт використовує модель дрона з Unity Asset Store, яку за ліцензією не можна зберігати в репозиторії.\n\n" +
            "1. Відкрийте сторінку асету й натисніть «Add to My Assets» (безкоштовно).\n" +
            "2. Window → Package Manager → My Assets → PBR Racing Drone → Download.\n" +
            "3. Меню Tools → FlyDrone → Import PBR Racing Drone (або перезапустіть редактор) — імпорт відбудеться автоматично.\n\n" +
            "Без асету проєкт працює, але дрон буде невидимим.",
            "Відкрити Asset Store", "Пізніше", "Вказати .unitypackage…");

        switch (choice)
        {
            case 0:
                Application.OpenURL(StoreUrl);
                break;
            case 2:
                var path = EditorUtility.OpenFilePanel(PackageName, "", "unitypackage");
                if (!string.IsNullOrEmpty(path)) AssetDatabase.ImportPackage(path, false);
                break;
        }
    }

    private static string FindCachedPackage()
    {
        return CacheRoots()
            .Where(Directory.Exists)
            .SelectMany(root =>
            {
                try { return Directory.EnumerateFiles(root, PackageFile, SearchOption.AllDirectories); }
                catch (Exception) { return Enumerable.Empty<string>(); }
            })
            .FirstOrDefault();
    }

    private static string[] CacheRoots()
    {
        // ASSETSTORE_CACHE_PATH — офіційний спосіб перенести кеш, має пріоритет
        var custom = Environment.GetEnvironmentVariable("ASSETSTORE_CACHE_PATH");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        return new[]
        {
            custom,
            Path.Combine(appData, "Unity", "Asset Store-5.x"),                            // Windows
            Path.Combine(home, "Library", "Unity", "Asset Store-5.x"),                    // macOS
            Path.Combine(home, ".local", "share", "unity3d", "Asset Store-5.x"),          // Linux
        }.Where(p => !string.IsNullOrEmpty(p)).ToArray();
    }
}
