// BalloonFlow — Episode → Firestore Uploader (standalone GUI)
//
// Assets/EditorData/Episodes 의 episode_*.json 을 Firestore /episodes 컬렉션에 업로드한다.
// firebase/seed/upload-episodes.js (Node Admin SDK) 와 '완전히 동일한' 문서 스키마/인코딩을 재현한다:
//   /episodes/{packageId}:
//     packageId : number
//     levelCount: number
//     version   : number   (episode.version || 1)
//     encoding  : "gzip+b64"
//     levelsJson: string    (base64( gzip( raw json ) ))  ← 클라: b64 decode + gunzip + JsonUtility.FromJson<LevelEpisode>
//     rawSize   : number    (raw json 문자 길이)
//     updatedAt : serverTimestamp
//
// 인증: 실행 파일과 같은 폴더의 service-account.json (Firebase 콘솔 → 프로젝트 설정 → 서비스 계정 → 새 비공개 키).
//       프로젝트 ID 는 키 파일의 project_id 에서 자동으로 읽는다.

using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Google.Cloud.Firestore;

namespace EpisodeUploader;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

internal sealed class MainForm : Form
{
    private const string Collection   = "episodes";
    private const int    SafetyBudget = 950_000; // base64 길이 상한(Firestore 1 MiB 문서 제한 여유분)

    private readonly TextBox  _txtFolder = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top };
    private readonly TextBox  _txtKey    = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top };
    private readonly CheckBox _chkDryRun = new() { Text = "Dry run (업로드 안 함, 미리보기만)", AutoSize = true };
    private readonly Button   _btnUpload = new() { Text = "업로드 시작", Height = 34, Anchor = AnchorStyles.Right | AnchorStyles.Top };
    private readonly TextBox  _txtLog    = new()
    {
        Multiline = true, ReadOnly = true, Dock = DockStyle.Fill,
        ScrollBars = ScrollBars.Vertical, WordWrap = false,
        BackColor = System.Drawing.Color.FromArgb(24, 24, 28),
        ForeColor = System.Drawing.Color.Gainsboro,
        Font = new System.Drawing.Font("Consolas", 9f),
    };

    public MainForm()
    {
        Text = "BalloonFlow — Episode → Firestore Uploader";
        Width = 820;
        Height = 560;
        MinimumSize = new System.Drawing.Size(560, 360);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new System.Drawing.Font("Segoe UI", 9.5f);

        var top = new TableLayoutPanel
        {
            Dock = DockStyle.Top, ColumnCount = 3, RowCount = 3,
            Height = 132, Padding = new Padding(10, 10, 10, 4), AutoSize = false,
        };
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));

        top.Controls.Add(new Label { Text = "Episode 폴더:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = false, Dock = DockStyle.Fill }, 0, 0);
        top.Controls.Add(_txtFolder, 1, 0);
        var btnBrowseFolder = new Button { Text = "찾아보기…", Dock = DockStyle.Fill };
        btnBrowseFolder.Click += (_, _) => BrowseFolder();
        top.Controls.Add(btnBrowseFolder, 2, 0);

        top.Controls.Add(new Label { Text = "service-account.json:", TextAlign = System.Drawing.ContentAlignment.MiddleLeft, AutoSize = false, Dock = DockStyle.Fill }, 0, 1);
        top.Controls.Add(_txtKey, 1, 1);
        var btnBrowseKey = new Button { Text = "찾아보기…", Dock = DockStyle.Fill };
        btnBrowseKey.Click += (_, _) => BrowseKey();
        top.Controls.Add(btnBrowseKey, 2, 1);

        top.Controls.Add(_chkDryRun, 1, 2);
        _btnUpload.Click += OnUpload;
        top.Controls.Add(_btnUpload, 2, 2);

        var logPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 0, 10, 10) };
        logPanel.Controls.Add(_txtLog);

        Controls.Add(logPanel);
        Controls.Add(top);

        // 기본값: 마지막 사용 경로 → 없으면 실행 폴더 옆의 service-account.json / episodes.
        var (folder, key) = LoadSettings();
        _txtFolder.Text = !string.IsNullOrEmpty(folder) ? folder : GuessDefaultEpisodeFolder();
        _txtKey.Text    = !string.IsNullOrEmpty(key)    ? key    : Path.Combine(AppContext.BaseDirectory, "service-account.json");

        Log("BalloonFlow Episode Uploader");
        Log("1) Episode 폴더(episode_*.json)를 지정하고  2) [업로드 시작] 을 누르세요.");
        Log("service-account.json 은 실행 파일 옆에 두면 자동으로 잡힙니다.");
        Log(new string('─', 60));
    }

    private static string GuessDefaultEpisodeFolder()
    {
        // 실행 폴더 옆 episodes → 없으면 실행 폴더 자체.
        string side = Path.Combine(AppContext.BaseDirectory, "episodes");
        return Directory.Exists(side) ? side : AppContext.BaseDirectory;
    }

    private void BrowseFolder()
    {
        using var dlg = new FolderBrowserDialog { Description = "episode_*.json 이 들어있는 폴더 선택" };
        if (Directory.Exists(_txtFolder.Text)) dlg.SelectedPath = _txtFolder.Text;
        if (dlg.ShowDialog(this) == DialogResult.OK) _txtFolder.Text = dlg.SelectedPath;
    }

    private void BrowseKey()
    {
        using var dlg = new OpenFileDialog { Filter = "service-account (*.json)|*.json|모든 파일 (*.*)|*.*", Title = "service-account.json 선택" };
        if (File.Exists(_txtKey.Text)) dlg.FileName = _txtKey.Text;
        if (dlg.ShowDialog(this) == DialogResult.OK) _txtKey.Text = dlg.FileName;
    }

    private async void OnUpload(object? sender, EventArgs e)
    {
        _btnUpload.Enabled = false;
        _txtLog.Clear();
        try
        {
            string folder  = _txtFolder.Text.Trim();
            string keyPath  = _txtKey.Text.Trim();
            bool   dryRun   = _chkDryRun.Checked;

            if (!Directory.Exists(folder)) { Log($"[오류] Episode 폴더가 없습니다: {folder}"); return; }
            if (!File.Exists(keyPath))
            {
                Log($"[오류] service-account.json 이 없습니다: {keyPath}");
                Log("  Firebase 콘솔 → 프로젝트 설정 → 서비스 계정 → '새 비공개 키 생성' 후");
                Log("  받은 파일을 service-account.json 으로 이 실행 파일 옆에 두거나 [찾아보기…] 로 지정하세요.");
                return;
            }

            var files = Directory.GetFiles(folder, "episode_*.json")
                .Where(f => Regex.IsMatch(Path.GetFileName(f), @"^episode_\d+\.json$"))
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToList();
            if (files.Count == 0) { Log($"[오류] {folder} 안에 episode_*.json 파일이 없습니다."); return; }

            SaveSettings(folder, keyPath);

            string keyJson;
            string projectId;
            try
            {
                keyJson = await File.ReadAllTextAsync(keyPath);
                projectId = JsonDocument.Parse(keyJson).RootElement.GetProperty("project_id").GetString() ?? "";
            }
            catch (Exception ex) { Log($"[오류] service-account.json 을 읽을 수 없습니다: {ex.Message}"); return; }
            if (string.IsNullOrEmpty(projectId)) { Log("[오류] service-account.json 에 project_id 가 없습니다."); return; }

            Log($"[시작] project={projectId}  collection={Collection}  files={files.Count}  dryRun={dryRun}");

            FirestoreDb? db = null;
            if (!dryRun)
            {
                try
                {
                    db = await new FirestoreDbBuilder { ProjectId = projectId, JsonCredentials = keyJson }.BuildAsync();
                }
                catch (Exception ex) { Log($"[오류] Firestore 연결 실패: {ex.Message}"); return; }
            }

            int ok = 0, skip = 0;
            foreach (var file in files)
            {
                string name = Path.GetFileName(file);
                string raw;
                try { raw = await File.ReadAllTextAsync(file); }
                catch (Exception ex) { Log($"  ! {name} 읽기 실패: {ex.Message}"); skip++; continue; }

                int packageId, levelCount, version;
                try
                {
                    using var jd = JsonDocument.Parse(raw);
                    var root = jd.RootElement;
                    if (!root.TryGetProperty("packageId", out var p) || p.ValueKind != JsonValueKind.Number)
                    { Log($"  ! {name} 스키마 오류 — packageId 없음"); skip++; continue; }
                    packageId = p.GetInt32();
                    if (!root.TryGetProperty("levels", out var l) || l.ValueKind != JsonValueKind.Array)
                    { Log($"  ! {name} 스키마 오류 — levels 배열 없음"); skip++; continue; }
                    levelCount = l.GetArrayLength();
                    version = (root.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.Number) ? v.GetInt32() : 1;
                    if (version == 0) version = 1; // node: ep.version || 1
                }
                catch (Exception ex) { Log($"  ! {name} JSON 파싱 실패: {ex.Message}"); skip++; continue; }

                string b64 = GzipBase64(raw);
                double ratio = raw.Length > 0 ? b64.Length * 100.0 / raw.Length : 0;
                Log($"  - pkg {packageId}  levels={levelCount}  raw={raw.Length}  gz+b64={b64.Length} ({ratio:F1}%)");

                if (b64.Length > SafetyBudget)
                { Log($"  ! pkg {packageId} 압축 후에도 {b64.Length} > {SafetyBudget} bytes — Firestore 1MiB 위험. 에피소드 분할 필요."); skip++; continue; }

                if (dryRun) continue;

                var docData = new Dictionary<string, object>
                {
                    ["packageId"]  = packageId,
                    ["levelCount"] = levelCount,
                    ["version"]    = version,
                    ["encoding"]   = "gzip+b64",
                    ["levelsJson"] = b64,
                    ["rawSize"]    = raw.Length,
                    ["updatedAt"]  = FieldValue.ServerTimestamp,
                };
                try
                {
                    await db!.Collection(Collection).Document(packageId.ToString()).SetAsync(docData);
                    Log($"    ✔ /{Collection}/{packageId} 업로드 완료");
                    ok++;
                }
                catch (Exception ex) { Log($"    ✘ pkg {packageId} 업로드 실패: {ex.Message}"); skip++; }
            }

            Log(new string('─', 60));
            Log($"[완료] 성공 {ok} · 건너뜀 {skip}{(dryRun ? "   (dry-run: 실제 업로드는 하지 않았습니다)" : "")}");
        }
        catch (Exception ex)
        {
            Log($"[예외] {ex.Message}");
        }
        finally
        {
            _btnUpload.Enabled = true;
        }
    }

    // raw json → base64( gzip( utf8 bytes ) ). node zlib.gzipSync(Z_BEST_COMPRESSION) 와 동일 포맷(gzip).
    private static string GzipBase64(string raw)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(raw);
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.SmallestSize, leaveOpen: true))
            gz.Write(bytes, 0, bytes.Length);
        return Convert.ToBase64String(ms.ToArray());
    }

    private void Log(string s)
    {
        if (_txtLog.InvokeRequired) { _txtLog.BeginInvoke(() => Log(s)); return; }
        _txtLog.AppendText(s + Environment.NewLine);
    }

    private static string SettingsPath => Path.Combine(AppContext.BaseDirectory, "episode-uploader.settings");

    private void SaveSettings(string folder, string key)
    {
        try { File.WriteAllLines(SettingsPath, new[] { folder, key }); } catch { /* 무시 */ }
    }

    private static (string folder, string key) LoadSettings()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var l = File.ReadAllLines(SettingsPath);
                return (l.ElementAtOrDefault(0) ?? "", l.ElementAtOrDefault(1) ?? "");
            }
        }
        catch { /* 무시 */ }
        return ("", "");
    }
}
