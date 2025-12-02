using System;
using System.IO;

namespace DLsiteRenamer
{
    public enum RenameItemType
    {
        Folder,
        ZipFile,
        RarFile
    }

    public class RenameItem
    {
        public string OriginalPath { get; set; }
        public string OriginalName { get; set; }
        public string NewName { get; set; }
        public string ProductId { get; set; }
        public RenameItemType ItemType { get; set; }
        public DLsiteInfo ProductInfo { get; set; }
        public bool IsInfoFetched { get; set; }
        public string ErrorMessage { get; set; }

        public string FullNewPath
        {
            get
            {
                if (string.IsNullOrEmpty(NewName))
                    return OriginalPath;

                if (ItemType == RenameItemType.Folder)
                {
                    var parentDir = Directory.GetParent(OriginalPath)?.FullName;
                    return parentDir != null ? Path.Combine(parentDir, NewName) : NewName;
                }
                else
                {
                    var directory = Path.GetDirectoryName(OriginalPath);
                    var extension = Path.GetExtension(OriginalPath);
                    return Path.Combine(directory ?? "", NewName + extension);
                }
            }
        }

        public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

        public string Status
        {
            get
            {
                if (HasError)
                    return "エラー";
                if (!IsInfoFetched)
                    return "情報取得待ち";
                if (string.IsNullOrEmpty(NewName))
                    return "リネーム名未設定";
                return "準備完了";
            }
        }
    }
}
