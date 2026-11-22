using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics.Drawables;
using Android.Graphics;
using Android.OS;
using Android.Provider;
using Android.Text;
using Android.Views;
using Android.Widget;
using ColorStateList = Android.Content.Res.ColorStateList;
using AssetStudioCLI;
using AssetStudioCLI.Options;

namespace AssetStudioModAndroid
{
    [Activity(Label = "AssetStudioMod", MainLauncher = true, Exported = true, Theme = "@style/AppTheme",
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.KeyboardHidden)]
    public class MainActivity : Activity
    {
        // ---- menu ids
        const int M_LoadFile = 1, M_LoadFolder = 2, M_Extract = 3, M_Reset = 4,
                  M_Export = 5, M_Options = 6, M_Sort = 7, M_SelAll = 8, M_SelNone = 9, M_About = 10, M_LoadSaf = 11;
        const int ReqSaf = 2001;

        readonly Handler _handler = new Handler(Looper.MainLooper);
        readonly StringBuilder _log = new StringBuilder();
        readonly object _logLock = new object();
        bool _logFlushPosted;
        bool _busy;
        int _previewSeq;
        string _lastDir = "/storage/emulated/0";

        // ---- views
        LinearLayout _assetsPanel, _hierPanel, _logPanel;
        EditText _search;
        Spinner _typeSpinner;
        ListView _assetList, _hierList;
        TextView _count, _hierHint, _logText, _status, _info, _textPreview, _subtitle;
        LinearLayout _empty;
        TextView[] _tabBtns;
        ImageView _logo;

        // ---- warna tema (selaras ikon: krem + arang, aksen amber)
        static readonly Color CBg = Color.ParseColor("#161614");
        static readonly Color CSurface = Color.ParseColor("#1F1F1C");
        static readonly Color CSurface2 = Color.ParseColor("#2B2B27");
        static readonly Color CStroke = Color.ParseColor("#3A3A34");
        static readonly Color CText = Color.ParseColor("#F4EEE0");
        static readonly Color CDim = Color.ParseColor("#A29D8E");
        static readonly Color CAccent = Color.ParseColor("#F2A93B");
        static readonly Color CAccentText = Color.ParseColor("#1A1A17");
        ScrollView _logScroll;
        ImageView _image;
        ProgressBar _progress;
        Bitmap _bitmap;

        // ---- data
        List<AssetItem> _view = new List<AssetItem>();
        readonly HashSet<AssetItem> _selected = new HashSet<AssetItem>();
        HashSet<BaseNode> _sceneFilter;
        string _sceneFilterName;
        string _typeFilter = "";
        List<string> _typeList = new List<string>();
        readonly Dictionary<Spinner, string[]> _spinnerOpts = new Dictionary<Spinner, string[]>();
        int _sortMode; // 0 asli, 1 nama, 2 tipe, 3 ukuran
        AssetAdapter _assetAdapter;
        readonly List<(BaseNode node, int depth)> _hierItems = new List<(BaseNode, int)>();
        readonly HashSet<BaseNode> _expanded = new HashSet<BaseNode>();
        StringListAdapter _hierAdapter;
        readonly List<string> _hierLabels = new List<string>();

        // =========================================================== lifecycle
        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            Engine.Init(FilesDir.AbsolutePath, CacheDir.AbsolutePath, AppendLog, SetProgress);
            BuildUi();
            RequestStorageAccess();
            AppendLog("AssetStudioMod Android siap. Menu (⋮) → Muat file / Muat folder.");
        }

        void RequestStorageAccess()
        {
            try
            {
                if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
                {
                    if (!Android.OS.Environment.IsExternalStorageManager)
                    {
                        new AlertDialog.Builder(this)
                            .SetTitle("Izin penyimpanan")
                            .SetMessage("Aplikasi butuh 'Akses semua file' untuk membaca file Unity dan menyimpan hasil export di penyimpanan internal.")
                            .SetPositiveButton("Buka pengaturan", (s, e) =>
                            {
                                var i = new Intent(Settings.ActionManageAppAllFilesAccessPermission,
                                    Android.Net.Uri.Parse("package:" + PackageName));
                                StartActivity(i);
                            })
                            .SetNegativeButton("Nanti", (s, e) => { })
                            .Show();
                    }
                }
                else
                {
                    RequestPermissions(new[]
                    {
                        Android.Manifest.Permission.ReadExternalStorage,
                        Android.Manifest.Permission.WriteExternalStorage
                    }, 100);
                }
            }
            catch (Exception ex) { AppendLog("[Warning] izin: " + ex.Message); }
        }

        // =========================================================== UI build
        int Dp(int v) => (int)(v * Resources.DisplayMetrics.Density + 0.5f);

        GradientDrawable Round(Color fill, int radiusDp, Color? stroke = null)
        {
            var g = new GradientDrawable();
            g.SetColor(fill);
            g.SetCornerRadius(Dp(radiusDp));
            if (stroke.HasValue) g.SetStroke(Dp(1), stroke.Value);
            return g;
        }

        Button Btn(string text, bool primary)
        {
            var b = new Button(this) { Text = text };
            b.SetAllCaps(false);
            b.TextSize = 15f;
            b.SetTypeface(null, TypefaceStyle.Bold);
            b.SetTextColor(primary ? CAccentText : CText);
            b.Background = primary ? Round(CAccent, 14) : Round(CSurface2, 14, CStroke);
            b.StateListAnimator = null;
            b.SetPadding(Dp(16), Dp(12), Dp(16), Dp(12));
            return b;
        }

        LinearLayout.LayoutParams Lp(int w, int h, float weight = 0f, int l = 0, int t = 0, int r = 0, int b = 0)
        {
            var lp = new LinearLayout.LayoutParams(w, h, weight);
            lp.SetMargins(Dp(l), Dp(t), Dp(r), Dp(b));
            return lp;
        }

        void BuildUi()
        {
            int MP = ViewGroup.LayoutParams.MatchParent;
            int WC = ViewGroup.LayoutParams.WrapContent;

            var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
            root.SetBackgroundColor(CBg);

            // ---------- header
            var header = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            header.SetGravity(GravityFlags.CenterVertical);
            header.SetPadding(Dp(16), Dp(14), Dp(12), Dp(10));
            _logo = new ImageView(this);
            var logoId = Resources.GetIdentifier("logo_tile", "drawable", PackageName);
            if (logoId != 0) _logo.SetImageResource(logoId);
            header.AddView(_logo, Lp(Dp(44), Dp(44)));
            var titleCol = new LinearLayout(this) { Orientation = Orientation.Vertical };
            var title = new TextView(this) { Text = "AssetStudioMod", TextSize = 20f };
            title.SetTextColor(CText); title.SetTypeface(null, TypefaceStyle.Bold);
            _subtitle = new TextView(this) { Text = "Android • siap", TextSize = 12f };
            _subtitle.SetTextColor(CDim);
            titleCol.AddView(title); titleCol.AddView(_subtitle);
            header.AddView(titleCol, Lp(0, WC, 1f, 12));
            var menuBtn = new TextView(this) { Text = "⋮", TextSize = 24f, Gravity = GravityFlags.Center };
            menuBtn.SetTextColor(CText);
            menuBtn.Background = Round(CSurface2, 22);
            menuBtn.Click += (s, e) => ShowMenu(menuBtn);
            header.AddView(menuBtn, Lp(Dp(44), Dp(44)));
            root.AddView(header);

            // ---------- tab (segmented)
            var tabRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            tabRow.Background = Round(CSurface, 16);
            tabRow.SetPadding(Dp(4), Dp(4), Dp(4), Dp(4));
            string[] names = { "Aset", "Hierarki", "Log" };
            _tabBtns = new TextView[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                int idx = i;
                var tv = new TextView(this) { Text = names[i], TextSize = 14f, Gravity = GravityFlags.Center };
                tv.SetPadding(0, Dp(10), 0, Dp(10));
                tv.Click += (s, e) => ShowTab(idx);
                _tabBtns[i] = tv;
                tabRow.AddView(tv, Lp(0, WC, 1f));
            }
            root.AddView(tabRow, Lp(MP, WC, 0f, 16, 2, 16, 8));

            var frame = new FrameLayout(this);
            root.AddView(frame, Lp(MP, 0, 1f));

            // ---------- panel aset
            _assetsPanel = new LinearLayout(this) { Orientation = Orientation.Vertical };

            var searchRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            searchRow.SetGravity(GravityFlags.CenterVertical);
            searchRow.Background = Round(CSurface, 14, CStroke);
            _search = new EditText(this) { Hint = "Cari nama / container", TextSize = 15f };
            _search.Background = null;
            _search.SetTextColor(CText); _search.SetHintTextColor(CDim);
            _search.SetSingleLine(true);
            _search.SetPadding(Dp(14), Dp(10), Dp(8), Dp(10));
            _search.TextChanged += (s, e) => ApplyFilter();
            searchRow.AddView(_search, Lp(0, WC, 1f));
            _typeSpinner = new Spinner(this);
            _typeSpinner.Background = Round(CSurface2, 10);
            _typeSpinner.SetPadding(Dp(8), Dp(4), Dp(4), Dp(4));
            _typeSpinner.ItemSelected += (s, e) =>
            {
                var v = e.Position >= 0 && e.Position < _typeList.Count ? _typeList[e.Position] : "Semua tipe";
                _typeFilter = v == "Semua tipe" ? "" : v;
                ApplyFilter();
            };
            searchRow.AddView(_typeSpinner, Lp(Dp(130), WC, 0f, 0, 0, 6));
            _assetsPanel.AddView(searchRow, Lp(MP, WC, 0f, 16, 0, 16, 0));

            _count = new TextView(this) { Text = "0 aset", TextSize = 12f };
            _count.SetTextColor(CDim);
            _count.SetPadding(Dp(20), Dp(8), Dp(20), Dp(4));
            _count.Click += (s, e) =>
            {
                if (_sceneFilter != null) { _sceneFilter = null; _sceneFilterName = null; ApplyFilter(); }
            };
            _assetsPanel.AddView(_count);

            var listFrame = new FrameLayout(this);
            _assetAdapter = new AssetAdapter(this);
            _assetList = new ListView(this) { Adapter = _assetAdapter };
            _assetList.Divider = null; _assetList.DividerHeight = 0;
            _assetList.SetPadding(Dp(12), 0, Dp(12), 0);
            _assetList.SetClipToPadding(false);
            _assetList.ItemClick += (s, e) => ShowPreview(e.Position);
            listFrame.AddView(_assetList, new FrameLayout.LayoutParams(MP, MP));

            _empty = new LinearLayout(this) { Orientation = Orientation.Vertical };
            _empty.SetGravity(GravityFlags.Center);
            var emptyLogo = new ImageView(this);
            if (logoId != 0) emptyLogo.SetImageResource(logoId);
            _empty.AddView(emptyLogo, Lp(Dp(96), Dp(96), 0f, 0, 0, 0, 12));
            var et1 = new TextView(this) { Text = "Belum ada aset", TextSize = 18f, Gravity = GravityFlags.Center };
            et1.SetTextColor(CText); et1.SetTypeface(null, TypefaceStyle.Bold);
            var et2 = new TextView(this) { Text = "Muat file Unity (assets, bundle, atau hasil ekstrak APK/OBB) untuk mulai.", TextSize = 13f, Gravity = GravityFlags.Center };
            et2.SetTextColor(CDim);
            et2.SetPadding(Dp(32), Dp(6), Dp(32), Dp(14));
            var emptyBtn = Btn("Muat file", true);
            emptyBtn.Click += (s, e) => ShowLoadMenu(emptyBtn);
            _empty.AddView(et1); _empty.AddView(et2); _empty.AddView(emptyBtn, Lp(WC, WC));
            listFrame.AddView(_empty, new FrameLayout.LayoutParams(MP, MP));
            _assetsPanel.AddView(listFrame, Lp(MP, 0, 1f));

            var previewScroll = new ScrollView(this);
            previewScroll.Background = Round(CSurface, 16, CStroke);
            var pv = new LinearLayout(this) { Orientation = Orientation.Vertical };
            pv.SetPadding(Dp(14), Dp(10), Dp(14), Dp(10));
            _info = new TextView(this) { Typeface = Typeface.Monospace, TextSize = 12f };
            _info.SetTextColor(CDim);
            _image = new ImageView(this);
            _image.SetAdjustViewBounds(true);
            _image.SetScaleType(ImageView.ScaleType.FitCenter);
            _textPreview = new TextView(this) { Typeface = Typeface.Monospace, TextSize = 11f };
            _textPreview.SetTextColor(CText);
            _textPreview.SetTextIsSelectable(true);
            pv.AddView(_info);
            pv.AddView(_image, new LinearLayout.LayoutParams(MP, WC));
            pv.AddView(_textPreview);
            previewScroll.AddView(pv);
            _assetsPanel.AddView(previewScroll, Lp(MP, 0, 0.8f, 16, 6, 16, 8));
            frame.AddView(_assetsPanel);

            // ---------- panel hierarki
            _hierPanel = new LinearLayout(this) { Orientation = Orientation.Vertical, Visibility = ViewStates.Gone };
            _hierHint = new TextView(this) { Text = "", TextSize = 12f };
            _hierHint.SetTextColor(CDim);
            _hierHint.SetPadding(Dp(20), Dp(6), Dp(20), Dp(8));
            _hierPanel.AddView(_hierHint);
            _hierAdapter = new StringListAdapter(this, _hierLabels);
            _hierList = new ListView(this) { Adapter = _hierAdapter };
            _hierList.Background = Round(CSurface, 16, CStroke);
            _hierList.Divider = new ColorDrawable(CStroke); _hierList.DividerHeight = 1;
            _hierList.ItemClick += (s, e) => ToggleHierNode(e.Position);
            _hierList.ItemLongClick += (s, e) => FilterByNode(e.Position);
            _hierPanel.AddView(_hierList, Lp(MP, 0, 1f, 16, 0, 16, 8));
            frame.AddView(_hierPanel);

            // ---------- panel log
            _logPanel = new LinearLayout(this) { Orientation = Orientation.Vertical, Visibility = ViewStates.Gone };
            _logScroll = new ScrollView(this);
            _logScroll.Background = Round(CSurface, 16, CStroke);
            _logText = new TextView(this) { Typeface = Typeface.Monospace, TextSize = 11f };
            _logText.SetTextColor(CText);
            _logText.SetPadding(Dp(14), Dp(10), Dp(14), Dp(10));
            _logText.SetTextIsSelectable(true);
            _logScroll.AddView(_logText);
            _logPanel.AddView(_logScroll, Lp(MP, 0, 1f, 16, 4, 16, 8));
            frame.AddView(_logPanel);

            // ---------- bawah: progress, status, aksi
            _progress = new ProgressBar(this, null, Android.Resource.Attribute.ProgressBarStyleHorizontal) { Max = 100 };
            _progress.ProgressTintList = ColorStateList.ValueOf(CAccent);
            _progress.ProgressBackgroundTintList = ColorStateList.ValueOf(CSurface2);
            _progress.SetPadding(0, 0, 0, 0);
            root.AddView(_progress, Lp(MP, Dp(4), 0f, 16, 0, 16, 0));
            _status = new TextView(this) { Text = "Siap", TextSize = 12f };
            _status.SetTextColor(CDim);
            _status.SetSingleLine(true);
            _status.SetPadding(Dp(20), Dp(6), Dp(20), Dp(2));
            root.AddView(_status);

            var actions = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            actions.SetPadding(Dp(16), Dp(6), Dp(16), Dp(12));
            var btnLoad = Btn("Muat file", false);
            btnLoad.Click += (s, e) => ShowLoadMenu(btnLoad);
            var btnExport = Btn("Export", true);
            btnExport.Click += (s, e) => ShowExportDialog();
            actions.AddView(btnLoad, Lp(0, WC, 1f, 0, 0, 6));
            actions.AddView(btnExport, Lp(0, WC, 1f, 6));
            root.AddView(actions);

            SetContentView(root);
            RefreshTypeSpinner();
            ShowTab(0);
            UpdateCount();
        }

        void ShowTab(int idx)
        {
            _assetsPanel.Visibility = idx == 0 ? ViewStates.Visible : ViewStates.Gone;
            _hierPanel.Visibility = idx == 1 ? ViewStates.Visible : ViewStates.Gone;
            _logPanel.Visibility = idx == 2 ? ViewStates.Visible : ViewStates.Gone;
            for (int i = 0; i < _tabBtns.Length; i++)
            {
                var on = i == idx;
                _tabBtns[i].Background = on ? Round(CAccent, 12) : null;
                _tabBtns[i].SetTextColor(on ? CAccentText : CDim);
                _tabBtns[i].SetTypeface(null, on ? TypefaceStyle.Bold : TypefaceStyle.Normal);
            }
            if (idx == 1) RebuildHierarchy();
            if (idx == 2) FlushLog();
        }

        // =========================================================== menu
        void ShowMenu(View anchor)
        {
            var pm = new PopupMenu(this, anchor);
            var m = pm.Menu;
            m.Add(0, M_LoadFile, 0, "Muat file");
            m.Add(0, M_LoadFolder, 1, "Muat folder");
            m.Add(0, M_LoadSaf, 2, "Muat file (picker sistem)");
            m.Add(0, M_Extract, 3, "Extract bundle");
            m.Add(0, M_Export, 4, "Export...");
            m.Add(0, M_SelAll, 5, "Pilih semua (terfilter)");
            m.Add(0, M_SelNone, 6, "Kosongkan pilihan");
            m.Add(0, M_Sort, 7, "Urutkan");
            m.Add(0, M_Options, 8, "Opsi");
            m.Add(0, M_Reset, 9, "Reset");
            m.Add(0, M_About, 10, "Tentang");
            pm.MenuItemClick += (s, e) => HandleMenu(e.Item.ItemId);
            pm.Show();
        }

        void ShowLoadMenu(View anchor)
        {
            var pm = new PopupMenu(this, anchor);
            pm.Menu.Add(0, M_LoadFile, 0, "Pilih file (penjelajah)");
            pm.Menu.Add(0, M_LoadFolder, 1, "Pilih folder");
            pm.Menu.Add(0, M_LoadSaf, 2, "Picker sistem (tanpa izin)");
            pm.MenuItemClick += (s, e) => HandleMenu(e.Item.ItemId);
            pm.Show();
        }

        void HandleMenu(int id)
        {
            switch (id)
            {
                case M_LoadFile: PickPath(false, p => LoadPaths(new List<string> { p })); break;
                case M_LoadSaf: PickSaf(); break;
                case M_LoadFolder: PickPath(true, p => LoadPaths(new List<string> { p })); break;
                case M_Extract: AskExtract(); break;
                case M_Export: ShowExportDialog(); break;
                case M_Options: ShowOptionsDialog(); break;
                case M_Sort: ShowSortDialog(); break;
                case M_Reset: DoReset(); break;
                case M_SelAll:
                    foreach (var a in _view) _selected.Add(a);
                    _assetAdapter.NotifyDataSetChanged(); UpdateCount(); break;
                case M_SelNone:
                    _selected.Clear(); _assetAdapter.NotifyDataSetChanged(); UpdateCount(); break;
                case M_About:
                    new AlertDialog.Builder(this).SetTitle("AssetStudioMod Android")
                        .SetMessage("Port Android dari AssetStudioMod (aelurum) / AssetStudio (Perfare), lisensi MIT.\n\n" +
                                    "Tidak tersedia di Android: export FBX/Animator (butuh FBX SDK), konversi audio FMOD (butuh libfmod.so), " +
                                    "bundle terkompresi Oodle (butuh libooz.so), pratinjau 3D.")
                        .SetPositiveButton("OK", (s, e) => { }).Show();
                    break;
            }
        }

        // =========================================================== jobs
        void RunJob(string title, Action work, Action done = null)
        {
            if (_busy) { Toast.MakeText(this, "Masih ada proses berjalan", ToastLength.Short).Show(); return; }
            _busy = true;
            RunOnUiThread(() => { _status.Text = title + "..."; _progress.Progress = 0; });
            Task.Run(() =>
            {
                try { work(); }
                catch (Exception ex)
                {
                    AppendLog("[Error] " + ex);
                    var msg = ex.GetType().Name + ": " + ex.Message;
                    RunOnUiThread(() => ShowMessage("Gagal: " + title, msg + "\n\nDetail lengkap ada di tab Log."));
                }
                finally
                {
                    _busy = false;
                    RunOnUiThread(() =>
                    {
                        try { done?.Invoke(); } catch (Exception ex) { AppendLog("[Error] " + ex.Message); }
                        _status.Text = title + " selesai";
                        _progress.Progress = 100;
                    });
                }
            });
        }

        void LoadPaths(List<string> paths)
        {
            _lastDir = Directory.Exists(paths[0]) ? paths[0] : (System.IO.Path.GetDirectoryName(paths[0]) ?? _lastDir);
            RunJob("Memuat aset", () => DoLoad(paths), AfterLoad);
        }

        void DoLoad(List<string> paths)
        {
            AppendLog("Memuat: " + string.Join(", ", paths));
            foreach (var p in paths)
                AppendLog(File.Exists(p) ? $"  {p} ({new FileInfo(p).Length} B)" : Directory.Exists(p) ? $"  {p} (folder)" : $"  {p} (TIDAK DITEMUKAN)");
            if (!Engine.Load(paths)) AppendLog("[Warning] Tidak ada file Unity yang bisa dimuat.");
        }

        void AfterLoad()
        {
            _selected.Clear(); _sceneFilter = null; _sceneFilterName = null;
            RefreshTypeSpinner();
            ApplyFilter();
            RebuildHierarchy();
            if (Engine.AllAssets.Count == 0)
                ShowMessage("Tidak ada aset dimuat",
                    "File tidak dikenali sebagai file Unity, atau tidak ada aset bertipe yang didukung.\n\n" +
                    "Coba: aktifkan 'Tampilkan semua tipe aset' di Opsi, isi 'Versi Unity', atau lihat tab Log. " +
                    "Bundle terkompresi Oodle tidak didukung tanpa libooz.so.");
        }

        // ---- picker sistem (SAF): tidak butuh izin penyimpanan, file disalin ke cache aplikasi
        void PickSaf()
        {
            var i = new Intent(Intent.ActionOpenDocument);
            i.AddCategory(Intent.CategoryOpenable);
            i.SetType("*/*");
            i.PutExtra(Intent.ExtraAllowMultiple, true);
            StartActivityForResult(i, ReqSaf);
        }

        protected override void OnActivityResult(int requestCode, Result resultCode, Intent data)
        {
            base.OnActivityResult(requestCode, resultCode, data);
            if (requestCode != ReqSaf || resultCode != Result.Ok || data == null) return;

            var uris = new List<Android.Net.Uri>();
            if (data.ClipData != null)
                for (int k = 0; k < data.ClipData.ItemCount; k++) uris.Add(data.ClipData.GetItemAt(k).Uri);
            else if (data.Data != null) uris.Add(data.Data);
            if (uris.Count == 0) return;

            RunJob("Menyalin & memuat", () =>
            {
                var dir = System.IO.Path.Combine(CacheDir.AbsolutePath, "import");
                try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
                Directory.CreateDirectory(dir);
                var locals = new List<string>();
                int idx = 0;
                foreach (var uri in uris)
                {
                    idx++;
                    var name = SafeName(GetDisplayName(uri));
                    var dst = System.IO.Path.Combine(dir, name);
                    AppendLog($"Menyalin ({idx}/{uris.Count}): {name}");
                    using (var input = ContentResolver.OpenInputStream(uri))
                    using (var output = File.Create(dst))
                    {
                        var buf = new byte[1 << 20];
                        int n;
                        while ((n = input.Read(buf, 0, buf.Length)) > 0) output.Write(buf, 0, n);
                    }
                    locals.Add(dst);
                }
                DoLoad(locals);
            }, AfterLoad);
        }

        string GetDisplayName(Android.Net.Uri uri)
        {
            try
            {
                using (var c = ContentResolver.Query(uri, null, null, null, null))
                {
                    if (c != null && c.MoveToFirst())
                    {
                        var ix = c.GetColumnIndex(OpenableColumns.DisplayName);
                        if (ix >= 0) return c.GetString(ix);
                    }
                }
            }
            catch { }
            return "file_" + Guid.NewGuid().ToString("N").Substring(0, 6);
        }

        static string SafeName(string n)
        {
            if (string.IsNullOrWhiteSpace(n)) n = "file";
            foreach (var ch in System.IO.Path.GetInvalidFileNameChars()) n = n.Replace(ch, '_');
            return n;
        }

        void ShowMessage(string title, string msg)
        {
            new AlertDialog.Builder(this).SetTitle(title).SetMessage(msg)
                .SetPositiveButton("OK", (s, e) => { })
                .SetNeutralButton("Lihat Log", (s, e) => ShowTab(2)).Show();
        }

        bool HasStorageAccess()
        {
            try
            {
                if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
                    return Android.OS.Environment.IsExternalStorageManager;
                return CheckSelfPermission(Android.Manifest.Permission.ReadExternalStorage) == Permission.Granted;
            }
            catch { return false; }
        }

        void DoReset()
        {
            if (_busy) return;
            Engine.Reset();
            _selected.Clear(); _sceneFilter = null; _sceneFilterName = null;
            _info.Text = ""; _textPreview.Text = ""; _image.SetImageBitmap(null);
            RefreshTypeSpinner(); ApplyFilter(); RebuildHierarchy();
            _status.Text = "Direset";
        }

        void AskExtract()
        {
            var items = new[] { "Extract satu file bundle", "Extract satu folder" };
            new AlertDialog.Builder(this).SetTitle("Extract / Decompress bundle")
                .SetItems(items, (s, e) =>
                {
                    bool folder = e.Which == 1;
                    PickPath(folder, p => RunJob("Extract bundle", () => Engine.Extract(p)));
                }).Show();
        }

        // =========================================================== filter / list
        void RefreshTypeSpinner()
        {
            var types = Engine.AllAssets.Select(a => a.TypeString).Distinct().OrderBy(x => x).ToList();
            types.Insert(0, "Semua tipe");
            _typeList = types;
            var ad = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerItem, types);
            ad.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
            _typeFilter = "";
            _typeSpinner.Adapter = ad;
        }

        void ApplyFilter()
        {
            IEnumerable<AssetItem> q = Engine.AllAssets;
            if (!string.IsNullOrEmpty(_typeFilter)) q = q.Where(a => a.TypeString == _typeFilter);
            var text = _search?.Text?.Trim();
            if (!string.IsNullOrEmpty(text))
                q = q.Where(a => (a.Text ?? "").IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 (a.Container ?? "").IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0);
            if (_sceneFilter != null)
                q = q.Where(a => a.Node != null && _sceneFilter.Contains(a.Node));
            switch (_sortMode)
            {
                case 1: q = q.OrderBy(a => a.Text, StringComparer.OrdinalIgnoreCase); break;
                case 2: q = q.OrderBy(a => a.TypeString).ThenBy(a => a.Text, StringComparer.OrdinalIgnoreCase); break;
                case 3: q = q.OrderByDescending(a => a.FullSize); break;
            }
            _view = q.ToList();
            _assetAdapter.NotifyDataSetChanged();
            UpdateCount();
        }

        void UpdateCount()
        {
            var t = $"{_view.Count} aset • {_selected.Count} terpilih";
            if (_sceneFilter != null) t += $" • filter hierarki: {_sceneFilterName} (tap untuk hapus)";
            _count.Text = t;
            var any = Engine.AllAssets.Count > 0;
            _empty.Visibility = any ? ViewStates.Gone : ViewStates.Visible;
            _assetList.Visibility = any ? ViewStates.Visible : ViewStates.Gone;
            _subtitle.Text = any ? $"{Engine.AllAssets.Count} aset dimuat" : "Android • siap";
        }

        void ShowSortDialog()
        {
            new AlertDialog.Builder(this).SetTitle("Urutkan")
                .SetSingleChoiceItems(new[] { "Urutan asli", "Nama", "Tipe", "Ukuran (besar dulu)" }, _sortMode, (s, e) =>
                {
                    _sortMode = e.Which; ApplyFilter(); ((Dialog)s).Dismiss();
                }).Show();
        }

        // =========================================================== preview
        void ShowPreview(int position)
        {
            if (position < 0 || position >= _view.Count) return;
            var item = _view[position];
            var seq = ++_previewSeq;
            _info.Text = "Memuat preview...";
            _textPreview.Text = "";
            Task.Run(() =>
            {
                var r = Engine.Preview(item);
                RunOnUiThread(() =>
                {
                    if (seq != _previewSeq) return;
                    _info.Text = r.Info;
                    _textPreview.Text = r.Text ?? "";
                    SetBitmap(r.Image);
                });
            });
        }

        void SetBitmap(byte[] png)
        {
            var old = _bitmap;
            _bitmap = null;
            if (png != null && png.Length > 0)
            {
                try
                {
                    var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
                    BitmapFactory.DecodeByteArray(png, 0, png.Length, bounds);
                    int sample = 1;
                    while (bounds.OutWidth / sample > 2048 || bounds.OutHeight / sample > 2048) sample *= 2;
                    _bitmap = BitmapFactory.DecodeByteArray(png, 0, png.Length, new BitmapFactory.Options { InSampleSize = sample });
                }
                catch (Exception ex) { AppendLog("[Warning] preview gambar: " + ex.Message); }
            }
            _image.SetImageBitmap(_bitmap);
            old?.Recycle();
        }

        // =========================================================== hierarchy
        void RebuildHierarchy()
        {
            _hierItems.Clear();
            void Walk(BaseNode n, int d)
            {
                _hierItems.Add((n, d));
                if (_expanded.Contains(n)) foreach (var c in n.nodes) Walk(c, d + 1);
            }
            foreach (var r in Engine.Tree) Walk(r, 0);
            _hierLabels.Clear();
            foreach (var (n, d) in _hierItems)
                _hierLabels.Add(new string(' ', d * 3) + (n.nodes.Count > 0 ? (_expanded.Contains(n) ? "▾ " : "▸ ") : "• ") + n.Text);
            _hierAdapter.NotifyDataSetChanged();
            _hierHint.Text = Engine.Tree.Count == 0
                ? "Hierarki kosong. Aktifkan 'Bangun hierarki scene' di Opsi lalu muat ulang file."
                : "Tap: buka/tutup node. Tahan: filter daftar aset ke node ini.";
        }

        void ToggleHierNode(int pos)
        {
            if (pos < 0 || pos >= _hierItems.Count) return;
            var n = _hierItems[pos].node;
            if (n.nodes.Count == 0) return;
            if (!_expanded.Remove(n)) _expanded.Add(n);
            RebuildHierarchy();
        }

        void FilterByNode(int pos)
        {
            if (pos < 0 || pos >= _hierItems.Count) return;
            var node = _hierItems[pos].node;
            var set = new HashSet<BaseNode>();
            void Collect(BaseNode n) { set.Add(n); foreach (var c in n.nodes) Collect(c); }
            Collect(node);
            _sceneFilter = set; _sceneFilterName = node.Text;
            ApplyFilter();
            ShowTab(0);
        }

        // =========================================================== export
        void ShowExportDialog()
        {
            if (Engine.AllAssets.Count == 0)
            {
                Toast.MakeText(this, "Belum ada aset dimuat", ToastLength.Short).Show();
                return;
            }
            var lay = new LinearLayout(this) { Orientation = Orientation.Vertical };
            lay.SetPadding(Dp(16), Dp(8), Dp(16), Dp(8));

            lay.AddView(new TextView(this) { Text = "Cakupan" });
            var scope = new Spinner(this);
            var scopes = new[] { $"Terpilih ({_selected.Count})", $"Semua yang terlihat ({_view.Count})", $"Semua aset ({Engine.AllAssets.Count})" };
            scope.Adapter = Arr(scopes);
            scope.SetSelection(_selected.Count > 0 ? 0 : 1);
            lay.AddView(scope);

            lay.AddView(new TextView(this) { Text = "Mode" });
            var mode = new Spinner(this);
            var modes = new[] { "Convert (png/wav/dll)", "Raw", "Dump (.txt)", "JSON dump", "Live2D Cubism", "Daftar aset (XML)" };
            mode.Adapter = Arr(modes);
            lay.AddView(mode);

            lay.AddView(new TextView(this) { Text = "Output: " + Engine.Settings.Output });

            new AlertDialog.Builder(this).SetTitle("Export").SetView(lay)
                .SetPositiveButton("Mulai", (s, e) =>
                {
                    List<AssetItem> list = scope.SelectedItemPosition == 0 ? _selected.ToList()
                        : scope.SelectedItemPosition == 1 ? new List<AssetItem>(_view)
                        : new List<AssetItem>(Engine.AllAssets);
                    int m = mode.SelectedItemPosition;
                    if (m != 4 && list.Count == 0) { Toast.MakeText(this, "Tidak ada aset untuk diexport", ToastLength.Short).Show(); return; }
                    switch (m)
                    {
                        case 0: RunJob("Export", () => Engine.Export(list, WorkMode.Export)); break;
                        case 1: RunJob("Export raw", () => Engine.Export(list, WorkMode.ExportRaw)); break;
                        case 2: RunJob("Dump", () => Engine.Export(list, WorkMode.Dump)); break;
                        case 3: RunJob("JSON dump", () => Engine.Export(list, WorkMode.JsonDump)); break;
                        case 4: RunJob("Export Live2D", () => Engine.ExportLive2D(), () => { RefreshTypeSpinner(); ApplyFilter(); }); break;
                        case 5: RunJob("Export daftar aset", () => Engine.ExportAssetListXml(list)); break;
                    }
                })
                .SetNegativeButton("Batal", (s, e) => { }).Show();
        }

        ArrayAdapter<string> Arr(string[] items)
        {
            var ad = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleSpinnerItem, items);
            ad.SetDropDownViewResource(Android.Resource.Layout.SimpleSpinnerDropDownItem);
            return ad;
        }

        // =========================================================== options
        void ShowOptionsDialog()
        {
            var st = Engine.Settings;
            var sv = new ScrollView(this);
            var lay = new LinearLayout(this) { Orientation = Orientation.Vertical };
            lay.SetPadding(Dp(16), Dp(8), Dp(16), Dp(8));
            sv.AddView(lay);

            EditText Edit(string label, string value, bool number = false)
            {
                lay.AddView(new TextView(this) { Text = label });
                var et = new EditText(this) { Text = value ?? "" };
                et.SetSingleLine(true);
                if (number) et.InputType = InputTypes.ClassNumber;
                lay.AddView(et);
                return et;
            }
            Spinner Spin(string label, string[] opts, string cur)
            {
                lay.AddView(new TextView(this) { Text = label });
                var sp = new Spinner(this) { Adapter = Arr(opts) };
                _spinnerOpts[sp] = opts;
                var ix = Array.IndexOf(opts, cur);
                sp.SetSelection(ix < 0 ? 0 : ix);
                lay.AddView(sp);
                return sp;
            }
            CheckBox Check(string label, bool v)
            {
                var cb = new CheckBox(this) { Text = label, Checked = v };
                lay.AddView(cb);
                return cb;
            }
            string Sel(Spinner sp) => _spinnerOpts[sp][Math.Max(0, sp.SelectedItemPosition)];

            var output = Edit("Folder output", st.Output);
            var img = Spin("Format gambar", new[] { "None", "Png", "Jpeg", "Bmp", "Tga", "Webp" }, st.ImageFormat);
            var aud = Spin("Format audio", Enum.GetNames(typeof(AudioFormat)), st.AudioFormat);
            var grp = Spin("Kelompokkan hasil export", Enum.GetNames(typeof(AssetGroupOption)), st.Group);
            var fnm = Spin("Format nama file", Enum.GetNames(typeof(FilenameFormat)), st.FilenameFormat);
            var spr = Spin("Cropping sprite", Enum.GetNames(typeof(SpriteCroppingMode)), st.SpriteCropping);
            var ovw = Check("Timpa file yang sudah ada", st.Overwrite);
            var tasks = Edit("Maks. task export paralel", st.MaxTasks.ToString(), true);
            var uv = Edit("Versi Unity (opsional, mis. 2022.3.2f1)", st.UnityVersion);
            var asm = Edit("Folder assembly (opsional, untuk MonoBehaviour)", st.AssemblyFolder);
            var all = Check("Tampilkan semua tipe aset (lebih lambat)", st.LoadAll);
            var hier = Check("Bangun hierarki scene saat memuat", st.BuildHierarchy);
            var dtd = Check("Dekompres bundle ke disk (hemat RAM)", st.DecompressToDisk);
            var nre = Check("Jangan pulihkan ekstensi TextAsset", st.NotRestoreExt);
            var atv = Check("Hindari parsing via TypeTree", st.AvoidTypetree);
            var rbm = Check("Raw byte array dari MonoBehaviour", st.RawBytesFromMono);

            lay.AddView(new TextView(this) { Text = "— Live2D —", Typeface = Typeface.DefaultBold });
            var l2g = Spin("Grup model", Enum.GetNames(typeof(CubismLive2DExtractor.Live2DModelGroupOption)), st.L2dGroup);
            var l2m = Spin("Mode motion", Enum.GetNames(typeof(CubismLive2DExtractor.Live2DMotionMode)), st.L2dMotion);
            var l2s = Check("Haluskan motion (Bezier)", st.L2dSmooth);
            var l2f = Check("Cari aset Live2D via nama file", st.L2dSearchByFilename);
            var l2t = Check("Filter tekstur ('texture' di nama)", st.L2dFilterTextures);

            var lvl = Spin("Level log", Enum.GetNames(typeof(AssetStudio.LoggerEvent)), st.LogLevel);

            new AlertDialog.Builder(this).SetTitle("Opsi").SetView(sv)
                .SetPositiveButton("Simpan", (s, e) =>
                {
                    st.Output = output.Text.Trim();
                    st.ImageFormat = Sel(img); st.AudioFormat = Sel(aud);
                    st.Group = Sel(grp); st.FilenameFormat = Sel(fnm); st.SpriteCropping = Sel(spr);
                    st.Overwrite = ovw.Checked;
                    st.MaxTasks = int.TryParse(tasks.Text, out var t) && t > 0 ? t : 1;
                    st.UnityVersion = uv.Text.Trim(); st.AssemblyFolder = asm.Text.Trim();
                    st.LoadAll = all.Checked; st.BuildHierarchy = hier.Checked; st.DecompressToDisk = dtd.Checked;
                    st.NotRestoreExt = nre.Checked; st.AvoidTypetree = atv.Checked; st.RawBytesFromMono = rbm.Checked;
                    st.L2dGroup = Sel(l2g); st.L2dMotion = Sel(l2m);
                    st.L2dSmooth = l2s.Checked; st.L2dSearchByFilename = l2f.Checked; st.L2dFilterTextures = l2t.Checked;
                    st.LogLevel = Sel(lvl);
                    st.Save(FilesDir.AbsolutePath);
                    Toast.MakeText(this, "Opsi disimpan (opsi muat berlaku saat muat ulang)", ToastLength.Short).Show();
                })
                .SetNegativeButton("Batal", (s, e) => { }).Show();
        }

        // =========================================================== file browser
        void PickPath(bool folder, Action<string> onPick)
        {
            if (!HasStorageAccess())
            {
                new AlertDialog.Builder(this)
                    .SetTitle("Akses file belum diberikan")
                    .SetMessage("Untuk menjelajah penyimpanan, aplikasi perlu izin 'Akses semua file'. " +
                                "Atau pakai 'Muat file (picker sistem)' yang tidak butuh izin (file disalin dulu ke cache).")
                    .SetPositiveButton("Buka pengaturan", (s, e) => RequestStorageAccess())
                    .SetNeutralButton("Picker sistem", (s, e) => PickSaf())
                    .SetNegativeButton("Batal", (s, e) => { }).Show();
                return;
            }
            var cur = Directory.Exists(_lastDir) ? _lastDir : "/storage/emulated/0";
            var entries = new List<(string label, string path, bool dir)>();
            var labels = new List<string>();

            var lay = new LinearLayout(this) { Orientation = Orientation.Vertical };
            var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            var edit = new EditText(this) { Text = cur };
            edit.SetSingleLine(true);
            var go = new Button(this) { Text = "Buka" };
            row.AddView(edit, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f));
            row.AddView(go);
            lay.AddView(row);
            var list = new ListView(this);
            var adapter = new StringListAdapter(this, labels);
            list.Adapter = adapter;
            lay.AddView(list, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(360)));

            AlertDialog dlg = null;

            void Nav(string dir)
            {
                try
                {
                    var dirs = Directory.GetDirectories(dir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase);
                    var files = folder ? Enumerable.Empty<string>()
                        : Directory.GetFiles(dir).OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
                    entries.Clear(); labels.Clear();
                    var parent = System.IO.Path.GetDirectoryName(dir.TrimEnd('/'));
                    entries.Add(("⬆ ..", string.IsNullOrEmpty(parent) ? "/" : parent, true));
                    foreach (var d in dirs) entries.Add(("📁 " + System.IO.Path.GetFileName(d), d, true));
                    foreach (var f in files) entries.Add(("📄 " + System.IO.Path.GetFileName(f), f, false));
                    foreach (var en in entries) labels.Add(en.label);
                    cur = dir; edit.Text = dir;
                    adapter.NotifyDataSetChanged();
                }
                catch (Exception ex)
                {
                    ShowMessage("Tidak bisa membuka folder", ex.Message + "\n\nFolder seperti Android/data tidak bisa dibuka di Android 11+. Pakai picker sistem.");
                }
            }

            go.Click += (s, e) => Nav(edit.Text.Trim());
            list.ItemClick += (s, e) =>
            {
                if (e.Position < 0 || e.Position >= entries.Count) return;
                var en = entries[e.Position];
                if (en.dir) Nav(en.path);
                else { dlg.Dismiss(); onPick(en.path); }
            };

            var b = new AlertDialog.Builder(this).SetTitle(folder ? "Pilih folder" : "Pilih file").SetView(lay)
                .SetNegativeButton("Batal", (s, e) => { });
            if (folder) b.SetPositiveButton("Pilih folder ini", (s, e) => onPick(cur));
            dlg = b.Create();
            Nav(cur);
            dlg.Show();
        }

        // =========================================================== log / progress
        void AppendLog(string line)
        {
            lock (_logLock)
            {
                _log.AppendLine(line);
                if (_log.Length > 90000) _log.Remove(0, _log.Length - 60000);
                if (_logFlushPosted) return;
                _logFlushPosted = true;
            }
            _handler.PostDelayed(new Java.Lang.Runnable(FlushLog), 300);
        }

        void FlushLog()
        {
            string text;
            lock (_logLock) { text = _log.ToString(); _logFlushPosted = false; }
            if (_logText == null) return;
            _logText.Text = text;
            _logScroll.Post(() => _logScroll.FullScroll(FocusSearchDirection.Down));
        }

        void SetProgress(int pct, string msg)
        {
            RunOnUiThread(() =>
            {
                _progress.Progress = Math.Max(0, Math.Min(100, pct));
                if (!string.IsNullOrEmpty(msg)) _status.Text = msg;
            });
        }

        // =========================================================== adapter
        static string TypeColorHex(string type)
        {
            switch (type)
            {
                case "Texture2D": return "#4FA3F7";
                case "Sprite": return "#3CCFB4";
                case "Mesh": return "#B07CF5";
                case "GameObject": return "#F2A93B";
                case "AudioClip": return "#F06FA0";
                case "TextAsset": return "#7FD36B";
                case "MonoBehaviour": return "#E87D5C";
                case "Material": return "#E0C060";
                case "Shader": return "#9AA7FF";
                case "AnimationClip": return "#FF8A65";
                case "Animator": return "#FFB74D";
                case "Font": return "#CFCFCF";
            }
            var pal = new[] { "#5DD0E8", "#C3A6FF", "#FFD166", "#7BDFB2", "#FF9AA2", "#A0C4FF" };
            return pal[(type.GetHashCode() & 0x7fffffff) % pal.Length];
        }

        // Membaca langsung dari List<string> terkini (ArrayAdapter<string> menyalin list saat dibuat,
        // sehingga perubahan list sesudahnya tidak tampil).
        class StringListAdapter : BaseAdapter<string>
        {
            readonly MainActivity _a;
            readonly List<string> _items;
            public StringListAdapter(MainActivity a, List<string> items) { _a = a; _items = items; }
            public override int Count => _items.Count;
            public override string this[int position] => _items[position];
            public override long GetItemId(int position) => position;

            public override View GetView(int position, View convertView, ViewGroup parent)
            {
                var tv = convertView as TextView;
                if (tv == null)
                {
                    tv = new TextView(_a) { TextSize = 15f };
                    tv.SetTextColor(CText);
                    tv.SetPadding(_a.Dp(14), _a.Dp(12), _a.Dp(14), _a.Dp(12));
                    tv.SetSingleLine(true);
                }
                tv.Text = _items[position];
                return tv;
            }
        }

        class RowView : LinearLayout
        {
            public int Pos;
            public CheckBox Check;
            public TextView Title, Sub, Chip;
            readonly LinearLayout _card;
            readonly MainActivity _a;

            public RowView(MainActivity a, Action<int, bool> onCheck) : base(a)
            {
                _a = a;
                Orientation = Orientation.Vertical;
                SetPadding(0, a.Dp(3), 0, a.Dp(3));

                _card = new LinearLayout(a) { Orientation = Orientation.Horizontal };
                _card.SetGravity(GravityFlags.CenterVertical);
                _card.SetPadding(a.Dp(6), a.Dp(10), a.Dp(12), a.Dp(10));

                Check = new CheckBox(a) { Focusable = false };
                Check.ButtonTintList = ColorStateList.ValueOf(CAccent);
                Check.Click += (s, e) => { onCheck(Pos, Check.Checked); ApplyStyle(Check.Checked); };
                _card.AddView(Check);

                var col = new LinearLayout(a) { Orientation = Orientation.Vertical };
                Title = new TextView(a) { TextSize = 15f };
                Title.SetTextColor(CText); Title.SetTypeface(null, TypefaceStyle.Bold);
                Title.SetSingleLine(true); Title.Ellipsize = TextUtils.TruncateAt.End;

                var line2 = new LinearLayout(a) { Orientation = Orientation.Horizontal };
                line2.SetGravity(GravityFlags.CenterVertical);
                Chip = new TextView(a) { TextSize = 10f };
                Chip.SetTextColor(CAccentText); Chip.SetTypeface(null, TypefaceStyle.Bold);
                Chip.SetPadding(a.Dp(7), a.Dp(2), a.Dp(7), a.Dp(2));
                Sub = new TextView(a) { TextSize = 11f };
                Sub.SetTextColor(CDim);
                Sub.SetSingleLine(true); Sub.Ellipsize = TextUtils.TruncateAt.End;
                line2.AddView(Chip, a.Lp(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent, 0f, 0, 3, 8, 0));
                line2.AddView(Sub, a.Lp(0, ViewGroup.LayoutParams.WrapContent, 1f, 0, 3));
                col.AddView(Title); col.AddView(line2);
                _card.AddView(col, a.Lp(0, ViewGroup.LayoutParams.WrapContent, 1f));

                AddView(_card, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
            }

            public void ApplyStyle(bool selected)
            {
                _card.Background = selected ? _a.Round(CSurface2, 14, CAccent) : _a.Round(CSurface, 14, CStroke);
            }

            public void SetChip(string type)
            {
                Chip.Text = type;
                Chip.Background = _a.Round(Color.ParseColor(TypeColorHex(type)), 6);
            }
        }

        class AssetAdapter : BaseAdapter<AssetItem>
        {
            readonly MainActivity _a;
            public AssetAdapter(MainActivity a) { _a = a; }
            public override int Count => _a._view.Count;
            public override AssetItem this[int position] => _a._view[position];
            public override long GetItemId(int position) => position;

            public override View GetView(int position, View convertView, ViewGroup parent)
            {
                var row = convertView as RowView ?? new RowView(_a, (pos, chk) =>
                {
                    if (pos < 0 || pos >= _a._view.Count) return;
                    var it = _a._view[pos];
                    if (chk) _a._selected.Add(it); else _a._selected.Remove(it);
                    _a.UpdateCount();
                });
                var item = _a._view[position];
                row.Pos = position;
                var sel = _a._selected.Contains(item);
                row.Check.Checked = sel;
                row.ApplyStyle(sel);
                row.Title.Text = string.IsNullOrEmpty(item.Text) ? "(tanpa nama)" : item.Text;
                row.SetChip(item.TypeString);
                row.Sub.Text = FormatSize(item.FullSize) +
                               (string.IsNullOrEmpty(item.Container) ? "" : " • " + item.Container);
                return row;
            }

            static string FormatSize(long b) =>
                b >= 1 << 20 ? $"{b / 1048576.0:0.0} MB" : b >= 1 << 10 ? $"{b / 1024.0:0.0} KB" : $"{b} B";
        }
    }
}
