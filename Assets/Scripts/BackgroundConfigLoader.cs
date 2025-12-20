using System;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;
using System.Collections;

public class BackgroundConfigLoader : MonoBehaviour
{
    [Header("References")]
    public SpriteRenderer backgroundRenderer;

    [Header("Config")]
    public string configFileName = "config.txt";
    public string backgroundKey = "background";

    private Sprite defaultSprite;

    void Awake()
    {
        if (backgroundRenderer == null)
            backgroundRenderer = GetComponent<SpriteRenderer>();

        // Lưu sprite mặc định
        defaultSprite = backgroundRenderer.sprite;
    }

    void Start()
    {
        // 👉 ĐƯỜNG DẪN DESKTOP
        string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        string configPath = Path.Combine(desktopPath, configFileName);

        Debug.Log("[Config] Reading config from: " + configPath);

        if (!File.Exists(configPath))
        {
            Debug.Log($"[Config] Không tìm thấy {configFileName} trên Desktop → dùng background mặc định");
            return;
        }

        string imagePathOrUrl = ReadBackgroundPath(configPath);
        if (string.IsNullOrEmpty(imagePathOrUrl))
        {
            Debug.Log("[Config] Không có key background → dùng background mặc định");
            return;
        }

        StartCoroutine(LoadBackground(imagePathOrUrl));
    }

    string ReadBackgroundPath(string path)
    {
        foreach (var line in File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.StartsWith("#")) continue;

            var parts = line.Split('=');
            if (parts.Length != 2) continue;

            if (parts[0].Trim().Equals(backgroundKey, StringComparison.OrdinalIgnoreCase))
            {
                return parts[1].Trim();
            }
        }
        return null;
    }

    IEnumerator LoadBackground(string pathOrUrl)
    {
        string url = pathOrUrl;

        // 👉 File local
        if (!pathOrUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            if (!File.Exists(pathOrUrl))
            {
                Debug.LogWarning("[Config] File ảnh không tồn tại → dùng background mặc định");
                yield break;
            }
            url = "file:///" + pathOrUrl.Replace("\\", "/");
        }

        using (UnityWebRequest req = UnityWebRequestTexture.GetTexture(url))
        {
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[Config] Load ảnh thất bại: {req.error}");
                yield break;
            }

            Texture2D tex = DownloadHandlerTexture.GetContent(req);
            Sprite newSprite = Sprite.Create(
                tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f),
                100f
            );

            backgroundRenderer.sprite = newSprite;
            Debug.Log("[Config] Background đã được thay từ Desktop");
        }
    }
}
