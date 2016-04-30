using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AlexCrome.Telligent.Azure.Filestorage
{
    public class FileStoreData
    {
        public FileStoreData(string fileStoreKey, bool isPublic)
        {
            FileStoreKey = fileStoreKey;
            IsPublic = isPublic;
        }

        public string FileStoreKey { get; }
        public bool IsPublic { get; }
    }


}
