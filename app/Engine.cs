using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using AssetStudio;
using AssetStudioCLI;
using AssetStudioCLI.Options;
using CubismLive2DExtractor;

namespace AssetStudioModAndroid
{
    /// <summary>Pengaturan (setara menu Options di versi desktop).</summary>
    public class AppSettings
    {
        public string Output { get; set; } = "/storage/emulated/0/AssetStudioMod/export";
        public string ImageFormat { get; set; } = "Png";        // None|Png|Jpeg|Bmp|Tga|Webp
        public string AudioFormat { get; set; } = "Wav";        // None|Wav
        public string Group { get; set; } = "ContainerPath";
        public string FilenameFormat { get; set; } = "AssetName";
        public string SpriteCropping { get; set; } = "SpriteRect";
        public bool Overwrite { get; set; }
        public int MaxTasks { get; set; } = Math.Max(1, Environment.ProcessorCount / 2);
        public string UnityVersion { get; set; } = "";
        public string AssemblyFolder { get; set; } = "";
        public bool LoadAll { get; set; }
        public bool BuildHierarchy { get; set; }
        public bool DecompressToDisk { get; set; }
        public bool NotRestoreExt { get; set; }
        public bool AvoidTypetree { get; set; }
        public bool RawBytesFromMono { get; set; }
        public string L2dGroup { get; set; } = "ContainerPath";
        public string L2dMotion { get; set; } = "MonoBehaviour";
        public bool L2dSmooth { get; set; }
        public bool L2dSearchByFilename { get; set; }
        public bool L2dFilterTextures { get; set; }
        public string LogLevel { get; set; } = "Info";

        static string PathFor(string filesDir) => System.IO.Path.Combine(filesDir, "settings.json");

        public static AppSettings Load(string filesDir)
        {
            try
            {
                var p = PathFor(filesDir);
                if (File.Exists(p))
                    return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(p)) ?? new AppSettings();
            }
            catch { }
            return new AppSettings();
        }

        public void Save(string filesDir)
        {
            try { File.WriteAllText(PathFor(filesDir), JsonSerializer.Serialize(this)); } catch { }
        }
    }

    public class PreviewResult
    {
        public string Info = "";
        public string Text;
        public byte[] Image;
    }

    /// <summary>Logger yang meneruskan log engine ke UI.</summary>
    internal class UiLogger : ILogger
    {
        public static Action<string> Sink = _ => { };
        public static LoggerEvent MinLevel = LoggerEvent.Info;

        public void Log(LoggerEvent loggerEvent, string message, bool ignoreLevel)
        {
            if (string.IsNullOrEmpty(message)) return;
            if (loggerEvent < MinLevel && !ignoreLevel) return;
            Sink($"[{loggerEvent}] {message.TrimEnd()}");
        }
    }

    /// <summary>Menangkap Console.Write engine (progress "[012%]" dan "Exported [x/y]") untuk progress bar/log.</summary>
    internal class ConsoleCapture : TextWriter
    {
        readonly Action<string> _log;
        readonly Action<int, string> _progress;
        readonly StringBuilder _buf = new StringBuilder();
        readonly object _lock = new object();
        static readonly System.Text.RegularExpressions.Regex Ansi =
            new System.Text.RegularExpressions.Regex(@"\u001b\[[0-9;]*m(?:\u001b\[K)?");
        static readonly System.Text.RegularExpressions.Regex Pct =
            new System.Text.RegularExpressions.Regex(@"\[(\d+)%\]");
        static readonly System.Text.RegularExpressions.Regex Cnt =
            new System.Text.RegularExpressions.Regex(@"\[(\d+)/(\d+)\]");

        public ConsoleCapture(Action<string> log, Action<int, string> progress)
        {
            _log = log; _progress = progress;
        }

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            lock (_lock)
            {
                if (value == '\n') { Flush(true); }
                else if (value == '\r') { Flush(false); }
                else _buf.Append(value);
            }
        }

        public override void Write(string value)
        {
            if (value == null) return;
            foreach (var c in value) Write(c);
        }

        public override void WriteLine(string value)
        {
            Write(value);
            Write('\n');
        }

        public override void WriteLine() => Write('\n');

        void Flush(bool asLogLine)
        {
            var s = Ansi.Replace(_buf.ToString(), "").TrimEnd();
            _buf.Clear();
            if (s.Length == 0) return;
            var m = Pct.Match(s);
            if (m.Success && s.Length < 12)
            {
                _progress(int.Parse(m.Groups[1].Value), null);
                return;
            }
            var c = Cnt.Match(s);
            if (c.Success && !asLogLine)
            {
                var total = int.Parse(c.Groups[2].Value);
                var cur = int.Parse(c.Groups[1].Value);
                _progress(total > 0 ? cur * 100 / total : 0, s);
                return;
            }
            _log(s);
        }
    }

    /// <summary>Lapisan tipis di atas engine AssetStudioMod CLI (Studio/Exporter/Options).</summary>
    internal static class Engine
    {
        public static AppSettings Settings = new AppSettings();
        public static string CacheDir = "";
        public static List<string> CurrentInput = new List<string>();
        public static List<AssetItem> AllAssets = new List<AssetItem>();
        public static List<BaseNode> Tree => Studio.gameObjectTree;

        static bool _inited;

        public static void Init(string filesDir, string cacheDir, Action<string> log, Action<int, string> progress)
        {
            if (_inited) return;
            _inited = true;
            // Semua thread (termasuk thread pool untuk export paralel) memakai format angka invariant (titik desimal).
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            System.Globalization.CultureInfo.DefaultThreadCurrentCulture = inv;
            System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = inv;
            System.Globalization.CultureInfo.CurrentCulture = inv;
            CacheDir = cacheDir;
            Directory.CreateDirectory(cacheDir);
            Settings = AppSettings.Load(filesDir);
            UiLogger.Sink = log;
            Console.SetOut(new ConsoleCapture(log, progress));
            // Harus diset sebelum kelas Studio pertama kali disentuh (static ctor membaca opsi ini).
            CLIOptions.o_cachePath.Value = cacheDir;
            Logger.Default = new UiLogger();
        }

        public static void ApplySettings()
        {
            var s = Settings;
            try { Directory.CreateDirectory(s.Output); } catch { }
            CLIOptions.o_outputFolder.Value = s.Output;
            CLIOptions.o_cachePath.Value = CacheDir;
            CLIOptions.f_overwriteExisting.Value = s.Overwrite;

            if (s.ImageFormat == "None") CLIOptions.convertTexture = false;
            else
            {
                CLIOptions.convertTexture = true;
                CLIOptions.o_imageFormat.Value = Enum.Parse<ImageFormat>(s.ImageFormat);
            }
            CLIOptions.o_audioFormat.Value = Enum.Parse<AudioFormat>(s.AudioFormat);
            CLIOptions.o_groupAssetsBy.Value = Enum.Parse<AssetGroupOption>(s.Group);
            CLIOptions.o_filenameFormat.Value = Enum.Parse<FilenameFormat>(s.FilenameFormat);
            CLIOptions.o_spriteCroppingMode.Value = Enum.Parse<SpriteCroppingMode>(s.SpriteCropping);
            CLIOptions.o_maxParallelExportTasks.Value = Math.Max(1, s.MaxTasks);
            CLIOptions.o_assemblyPath.Value = s.AssemblyFolder ?? "";
            CLIOptions.f_loadAllAssets.Value = s.LoadAll;
            CLIOptions.f_decompressToDisk.Value = s.DecompressToDisk;
            CLIOptions.f_notRestoreExtensionName.Value = s.NotRestoreExt;
            CLIOptions.f_avoidLoadingViaTypetree.Value = s.AvoidTypetree;
            CLIOptions.f_rawByteArrayFromMono.Value = s.RawBytesFromMono;
            CLIOptions.o_l2dGroupOption.Value = Enum.Parse<Live2DModelGroupOption>(s.L2dGroup);
            CLIOptions.o_l2dMotionMode.Value = Enum.Parse<Live2DMotionMode>(s.L2dMotion);
            CLIOptions.f_l2dForceBezier.Value = s.L2dSmooth;
            CLIOptions.f_l2dAssetSearchByFilename.Value = s.L2dSearchByFilename;
            CLIOptions.f_l2dFilterTextures.Value = s.L2dFilterTextures;
            CLIOptions.o_logLevel.Value = Enum.Parse<LoggerEvent>(s.LogLevel);
            UiLogger.MinLevel = CLIOptions.o_logLevel.Value;
            CLIOptions.o_unityVersion.Value = string.IsNullOrWhiteSpace(s.UnityVersion)
                ? null : new UnityVersion(s.UnityVersion.Trim());
            CLIOptions.filterBy = FilterBy.None;

            var o = Studio.assetsManager.Options;
            o.CustomCachePath = CacheDir;
            o.DecompressToDisk = s.DecompressToDisk;
            o.ParseByTypeTree = !s.AvoidTypetree;
            o.GameSpecific.CustomUnityVersion = CLIOptions.o_unityVersion.Value;
        }

        public static bool Load(List<string> paths, WorkMode mode = WorkMode.Export)
        {
            ApplySettings();
            Studio.Clear();
            Studio.parsedAssetsList = new List<AssetItem>();
            Studio.gameObjectTree.Clear();
            AllAssets = new List<AssetItem>();

            CLIOptions.o_workMode.Value = mode;
            CLIOptions.inputPathList = new List<string>(paths);
            CurrentInput = new List<string>(paths);
            // Hierarki scene hanya dibangun engine bila grup = SceneHierarchy
            if (Settings.BuildHierarchy && mode == WorkMode.Export)
                CLIOptions.o_groupAssetsBy.Value = AssetGroupOption.SceneHierarchy;

            if (!Studio.LoadAssets()) return false;
            Studio.ParseAssets();
            AllAssets = new List<AssetItem>(Studio.parsedAssetsList);
            return true;
        }

        public static void Export(List<AssetItem> items, WorkMode mode)
        {
            ApplySettings();
            CLIOptions.o_workMode.Value = mode;
            var backup = Studio.parsedAssetsList;
            var list = items;
            if (mode == WorkMode.Export || mode == WorkMode.Dump || mode == WorkMode.JsonDump)
            {
                var ok = CLIOptions.o_exportAssetTypes.Value;
                list = items.Where(x => ok.Contains(x.Type)).ToList();
            }
            Studio.parsedAssetsList = new List<AssetItem>(list);
            try { Studio.ExportAssets(); }
            finally { Studio.parsedAssetsList = backup; }
        }

        public static void ExportAssetListXml(List<AssetItem> items)
        {
            ApplySettings();
            CLIOptions.o_exportAssetList.Value = ExportListType.XML;
            var backup = Studio.parsedAssetsList;
            Studio.parsedAssetsList = new List<AssetItem>(items);
            try { Studio.ExportAssetList(); }
            finally { Studio.parsedAssetsList = backup; CLIOptions.o_exportAssetList.Value = ExportListType.None; }
        }

        public static void ExportLive2D()
        {
            var paths = new List<string>(CurrentInput);
            if (Load(paths, WorkMode.Live2D))
            {
                Studio.ExportLive2D();
            }
            // kembalikan daftar aset normal
            Load(paths, WorkMode.Export);
        }

        public static void Extract(string path)
        {
            ApplySettings();
            CLIOptions.o_workMode.Value = WorkMode.Extract;
            CLIOptions.inputPathList = new List<string> { path };
            Studio.ExtractBundles();
        }

        public static void Reset()
        {
            Studio.Clear();
            Studio.parsedAssetsList = new List<AssetItem>();
            Studio.gameObjectTree.Clear();
            AllAssets = new List<AssetItem>();
            CurrentInput = new List<string>();
        }

        // ---------------------------------------------------------------- Preview
        public static PreviewResult Preview(AssetItem item)
        {
            var r = new PreviewResult();
            var sb = new StringBuilder();
            sb.AppendLine($"Name      : {item.Text}");
            sb.AppendLine($"Type      : {item.TypeString}");
            sb.AppendLine($"PathID    : {item.m_PathID}");
            sb.AppendLine($"Size      : {item.FullSize} B");
            if (!string.IsNullOrEmpty(item.Container)) sb.AppendLine($"Container : {item.Container}");
            sb.AppendLine($"Source    : {item.SourceFile?.fileName}");

            try
            {
                switch (item.Asset)
                {
                    case Texture2D t:
                        sb.AppendLine($"Texture   : {t.m_Width}x{t.m_Height} {t.m_TextureFormat}");
                        if (t.TryConvertToImage(true, out var timg))
                        {
                            using (timg) r.Image = EncodePng(timg);
                        }
                        else sb.AppendLine("(tidak dapat dikonversi untuk preview)");
                        break;
                    case Sprite sp:
                        if (sp.TryGetImage(out var simg))
                        {
                            using (simg)
                            {
                                sb.AppendLine($"Sprite    : {simg.Width}x{simg.Height}");
                                r.Image = EncodePng(simg);
                            }
                        }
                        else sb.AppendLine("(tidak dapat dikonversi untuk preview)");
                        break;
                    case TextAsset ta:
                        r.Text = Truncate(DecodeText(ta.m_Script));
                        break;
                    case AudioClip _:
                        sb.AppendLine("(pemutar audio tidak tersedia di versi Android; gunakan Export)");
                        break;
                    case Font _:
                    case Mesh _:
                        sb.AppendLine("(preview tidak tersedia; gunakan Export)");
                        break;
                    default:
                        r.Text = Truncate(SafeDump(item.Asset));
                        break;
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("Preview gagal: " + ex.Message);
            }
            r.Info = sb.ToString();
            return r;
        }

        static byte[] EncodePng(SixLabors.ImageSharp.Image img)
        {
            using (var ms = ImageExtensions.ConvertToStream(img, ImageFormat.Png))
                return ms.ToArray();
        }

        static string DecodeText(byte[] data)
        {
            if (data == null) return "";
            var n = Math.Min(data.Length, 60000);
            return Encoding.UTF8.GetString(data, 0, n);
        }

        static string SafeDump(AssetStudio.Object obj)
        {
            try { return obj.Dump(); } catch { }
            try { return obj.DumpObject(); } catch { }
            return null;
        }

        static string Truncate(string s, int max = 60000)
        {
            if (s == null) return null;
            return s.Length <= max ? s : s.Substring(0, max) + "\n... (dipotong)";
        }
    }
}
