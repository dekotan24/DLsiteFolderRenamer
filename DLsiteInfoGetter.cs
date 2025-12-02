using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace DLsiteRenamer
{
    public class DLsiteInfo
    {
        public string ProductId { get; set; }
        public string ProductName { get; set; }
        public string CircleName { get; set; }
        public DateTime SaleDate { get; set; }
        public List<string> CVs { get; set; }

        public DLsiteInfo()
        {
            CVs = new List<string>();
        }
    }

    public class DLsiteInfoGetter
    {
        // HttpClientは使いまわすのが推奨されているため、staticで保持
        private static readonly HttpClient client = new HttpClient();

        static DLsiteInfoGetter()
        {
            // User-Agentを設定（DLsite APIへの礼儀として）
            client.DefaultRequestHeaders.Add("User-Agent", "DLsiteRenamer/1.0");
        }

        public static async Task<DLsiteInfo> GetProductInfoAsync(string productId)
        {
            try
            {
                // DLsite API エンドポイント
                string url = $"https://www.dlsite.com/maniax/product/info/ajax?product_id={productId}";

                var response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var content = await response.Content.ReadAsStringAsync();
                
                using (var jsonDoc = JsonDocument.Parse(content))
                {
                    var root = jsonDoc.RootElement;

                    // product_id をキーとして情報を取得
                    if (!root.TryGetProperty(productId, out var productElement))
                    {
                        return null;
                    }

                    var info = new DLsiteInfo
                    {
                        ProductId = productId
                    };

                    // 作品名
                    if (productElement.TryGetProperty("work_name", out var workName))
                    {
                        info.ProductName = workName.GetString();
                    }

                    // サークル名
                    if (productElement.TryGetProperty("maker_name", out var makerName))
                    {
                        info.CircleName = makerName.GetString();
                    }

                    // 販売日
                    if (productElement.TryGetProperty("regist_date", out var registDate))
                    {
                        var dateStr = registDate.GetString();
                        if (DateTime.TryParse(dateStr, out var date))
                        {
                            info.SaleDate = date;
                        }
                    }

                    // 声優情報
                    if (productElement.TryGetProperty("cvs", out var cvsElement))
                    {
                        if (cvsElement.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var cv in cvsElement.EnumerateArray())
                            {
                                if (cv.TryGetProperty("name", out var cvName))
                                {
                                    info.CVs.Add(cvName.GetString());
                                }
                            }
                        }
                    }

                    return info;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// ファイル名やフォルダ名からDLsite作品IDを抽出
        /// </summary>
        public static string ExtractProductId(string name)
        {
            // RJ, VJ, BJ, RG などのパターンをサポート
            var patterns = new[] { "RJ", "VJ", "BJ", "RG", "RE" };
            
            foreach (var pattern in patterns)
            {
                int index = name.IndexOf(pattern, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                {
                    // パターンの後の数字を抽出（6桁または8桁）
                    string remaining = name.Substring(index);
                    string numbersOnly = "";
                    
                    for (int i = pattern.Length; i < remaining.Length && char.IsDigit(remaining[i]); i++)
                    {
                        numbersOnly += remaining[i];
                    }

                    // 6桁または8桁の数字があれば有効
                    if (numbersOnly.Length == 6 || numbersOnly.Length == 8)
                    {
                        return pattern.ToUpper() + numbersOnly;
                    }
                }
            }

            return null;
        }
    }
}
