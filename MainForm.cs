using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DLsiteRenamer
{
    public partial class MainForm : Form
    {
        private List<RenameItem> renameItems = new List<RenameItem>();
        private bool isProcessing = false;

        public MainForm()
        {
            InitializeComponent();
        }

        private void btnBrowse_Click(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "対象フォルダを選択してください";
                if (!string.IsNullOrEmpty(txtTargetFolder.Text) && Directory.Exists(txtTargetFolder.Text))
                {
                    dialog.SelectedPath = txtTargetFolder.Text;
                }

                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    txtTargetFolder.Text = dialog.SelectedPath;
                }
            }
        }

        private void btnHelp_Click(object sender, EventArgs e)
        {
            var help = new StringBuilder();
            help.AppendLine("使用可能なプレースホルダー：");
            help.AppendLine();
            help.AppendLine("{ProductID} - 作品ID（例：RJ123456）");
            help.AppendLine("{ProductName} - 作品名");
            help.AppendLine("{ProductCircle} - サークル名");
            help.AppendLine("{ProductDate} - 販売日（yyMMdd形式）");
            help.AppendLine("{ProductCVs} - 声優（複数の場合は「、」で区切られます）");
            help.AppendLine();
            help.AppendLine("デフォルト：[{ProductID}] [{ProductCircle}] {ProductName}");
            help.AppendLine();
            help.AppendLine("例：");
            help.AppendLine("[RJ123456] [サークル名] 作品タイトル");

            MessageBox.Show(help.ToString(), "命名規則ヘルプ", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private async void btnScan_Click(object sender, EventArgs e)
        {
            if (isProcessing)
            {
                AddLog("処理中です。完了するまでお待ちください。");
                return;
            }

            if (string.IsNullOrWhiteSpace(txtTargetFolder.Text))
            {
                MessageBox.Show("対象フォルダを指定してください。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!Directory.Exists(txtTargetFolder.Text))
            {
                MessageBox.Show("指定されたフォルダが存在しません。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                isProcessing = true;
                btnScan.Enabled = false;
                btnRename.Enabled = false;
                renameItems.Clear();
                lvItems.Items.Clear();
                txtLog.Clear();

                AddLog("スキャン開始...");

                // スキャン実行
                await Task.Run(() => ScanDirectory(txtTargetFolder.Text, chkIncludeSubfolders.Checked));

                AddLog($"{renameItems.Count} 個のアイテムを検出しました。");

                if (renameItems.Count == 0)
                {
                    AddLog("DLsite作品IDを含むファイル/フォルダが見つかりませんでした。");
                    return;
                }

                // 作品情報を取得
                AddLog("作品情報を取得中...");
                progressBar.Maximum = renameItems.Count;
                progressBar.Value = 0;

                for (int i = 0; i < renameItems.Count; i++)
                {
                    var item = renameItems[i];
                    AddLog($"作品情報取得中: {item.ProductId}");

                    var info = await DLsiteInfoGetter.GetProductInfoAsync(item.ProductId);
                    if (info != null)
                    {
                        item.ProductInfo = info;
                        item.IsInfoFetched = true;
                        item.NewName = ApplyNamingPattern(info);
                        
                        // ファイル名として使用できない文字を置換
                        item.NewName = SanitizeFileName(item.NewName);
                    }
                    else
                    {
                        item.ErrorMessage = "作品情報の取得に失敗しました";
                        AddLog($"エラー: {item.ProductId} の情報取得に失敗しました");
                    }

                    progressBar.Value = i + 1;
                }

                // リストビューに表示
                UpdateListView();

                AddLog("スキャン完了。");
                btnRename.Enabled = renameItems.Any(x => x.IsInfoFetched && !x.HasError);
            }
            catch (Exception ex)
            {
                AddLog($"エラー: {ex.Message}");
                MessageBox.Show($"スキャン中にエラーが発生しました：\n{ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                isProcessing = false;
                btnScan.Enabled = true;
                progressBar.Value = 0;
            }
        }

        private void ScanDirectory(string path, bool includeSubfolders)
        {
            var searchOption = includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            // ZIPファイルを検索（.part以外）
            var zipFiles = Directory.GetFiles(path, "*.zip", searchOption)
                .Where(f => !f.Contains(".part", StringComparison.OrdinalIgnoreCase));

            foreach (var file in zipFiles)
            {
                var productId = DLsiteInfoGetter.ExtractProductId(Path.GetFileNameWithoutExtension(file));
                if (productId != null)
                {
                    renameItems.Add(new RenameItem
                    {
                        OriginalPath = file,
                        OriginalName = Path.GetFileName(file),
                        ProductId = productId,
                        ItemType = RenameItemType.ZipFile
                    });
                }
            }

            // RARファイルを検索（.part以外）
            var rarFiles = Directory.GetFiles(path, "*.rar", searchOption)
                .Where(f => !f.Contains(".part", StringComparison.OrdinalIgnoreCase));

            foreach (var file in rarFiles)
            {
                var productId = DLsiteInfoGetter.ExtractProductId(Path.GetFileNameWithoutExtension(file));
                if (productId != null)
                {
                    renameItems.Add(new RenameItem
                    {
                        OriginalPath = file,
                        OriginalName = Path.GetFileName(file),
                        ProductId = productId,
                        ItemType = RenameItemType.RarFile
                    });
                }
            }

            // フォルダを検索
            var directories = Directory.GetDirectories(path, "*", searchOption);
            foreach (var dir in directories)
            {
                var folderName = Path.GetFileName(dir);
                var productId = DLsiteInfoGetter.ExtractProductId(folderName);
                if (productId != null)
                {
                    renameItems.Add(new RenameItem
                    {
                        OriginalPath = dir,
                        OriginalName = folderName,
                        ProductId = productId,
                        ItemType = RenameItemType.Folder
                    });
                }
            }
        }

        private string ApplyNamingPattern(DLsiteInfo info)
        {
            var pattern = txtNamingPattern.Text;
            var result = pattern;

            result = result.Replace("{ProductID}", info.ProductId ?? "");
            result = result.Replace("{ProductName}", info.ProductName ?? "");
            result = result.Replace("{ProductCircle}", info.CircleName ?? "");
            result = result.Replace("{ProductDate}", info.SaleDate.ToString("yyMMdd"));
            
            if (info.CVs != null && info.CVs.Count > 0)
            {
                result = result.Replace("{ProductCVs}", string.Join("、", info.CVs));
            }
            else
            {
                result = result.Replace("{ProductCVs}", "");
            }

            return result.Trim();
        }

        private string SanitizeFileName(string fileName)
        {
            // Windowsで使用できない文字を置換
            var invalidChars = Path.GetInvalidFileNameChars();
            foreach (var c in invalidChars)
            {
                fileName = fileName.Replace(c, '_');
            }

            // その他の問題のある文字も置換
            fileName = fileName.Replace(":", "：");  // 全角コロン
            fileName = fileName.Replace("?", "？");  // 全角クエスチョン
            fileName = fileName.Replace("*", "＊");  // 全角アスタリスク
            fileName = fileName.Replace("\"", """);  // 全角引用符
            fileName = fileName.Replace("<", "＜");  // 全角小なり
            fileName = fileName.Replace(">", "＞");  // 全角大なり
            fileName = fileName.Replace("|", "｜");  // 全角パイプ

            return fileName;
        }

        private void UpdateListView()
        {
            lvItems.Items.Clear();

            foreach (var item in renameItems)
            {
                var listItem = new ListViewItem(GetItemTypeText(item.ItemType));
                listItem.SubItems.Add(item.OriginalName);
                listItem.SubItems.Add(item.NewName ?? "(取得失敗)");
                listItem.SubItems.Add(item.ProductId);
                listItem.SubItems.Add(item.Status);

                if (item.HasError)
                {
                    listItem.ForeColor = System.Drawing.Color.Red;
                }

                lvItems.Items.Add(listItem);
            }
        }

        private string GetItemTypeText(RenameItemType type)
        {
            switch (type)
            {
                case RenameItemType.Folder:
                    return "フォルダ";
                case RenameItemType.ZipFile:
                    return "ZIP";
                case RenameItemType.RarFile:
                    return "RAR";
                default:
                    return "不明";
            }
        }

        private async void btnRename_Click(object sender, EventArgs e)
        {
            if (isProcessing)
            {
                AddLog("処理中です。完了するまでお待ちください。");
                return;
            }

            var validItems = renameItems.Where(x => x.IsInfoFetched && !x.HasError).ToList();
            if (validItems.Count == 0)
            {
                MessageBox.Show("リネーム可能なアイテムがありません。", "情報", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var result = MessageBox.Show(
                $"{validItems.Count} 個のアイテムをリネームします。\n\n実行してよろしいですか？",
                "確認",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (result != DialogResult.Yes)
            {
                return;
            }

            try
            {
                isProcessing = true;
                btnScan.Enabled = false;
                btnRename.Enabled = false;

                AddLog("リネーム処理を開始します...");
                progressBar.Maximum = validItems.Count;
                progressBar.Value = 0;

                int successCount = 0;
                int failCount = 0;

                foreach (var item in validItems)
                {
                    try
                    {
                        var newPath = item.FullNewPath;

                        // 既に同名のファイル/フォルダが存在する場合はスキップ
                        if (File.Exists(newPath) || Directory.Exists(newPath))
                        {
                            AddLog($"スキップ: {item.OriginalName} （既に存在します）");
                            failCount++;
                            continue;
                        }

                        if (item.ItemType == RenameItemType.Folder)
                        {
                            Directory.Move(item.OriginalPath, newPath);
                        }
                        else
                        {
                            File.Move(item.OriginalPath, newPath);
                        }

                        AddLog($"成功: {item.OriginalName} → {Path.GetFileName(newPath)}");
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        AddLog($"失敗: {item.OriginalName} - {ex.Message}");
                        failCount++;
                    }

                    progressBar.Value++;
                }

                AddLog($"リネーム完了: 成功 {successCount} 件、失敗 {failCount} 件");
                MessageBox.Show(
                    $"リネームが完了しました。\n\n成功: {successCount} 件\n失敗: {failCount} 件",
                    "完了",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                // 再スキャン
                btnClear_Click(sender, e);
            }
            catch (Exception ex)
            {
                AddLog($"エラー: {ex.Message}");
                MessageBox.Show($"リネーム中にエラーが発生しました：\n{ex.Message}", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                isProcessing = false;
                btnScan.Enabled = true;
                progressBar.Value = 0;
            }
        }

        private void btnClear_Click(object sender, EventArgs e)
        {
            renameItems.Clear();
            lvItems.Items.Clear();
            txtLog.Clear();
            btnRename.Enabled = false;
            AddLog("クリアしました。");
        }

        private void AddLog(string message)
        {
            if (txtLog.InvokeRequired)
            {
                txtLog.Invoke(new Action(() => AddLog(message)));
                return;
            }

            txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\r\n");
            txtLog.SelectionStart = txtLog.Text.Length;
            txtLog.ScrollToCaret();
        }
    }
}
