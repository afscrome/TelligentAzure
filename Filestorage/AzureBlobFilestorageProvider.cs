using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Telligent.Evolution.Extensibility.Storage.Version1;
using System;
using System.Collections;
using System.Net;
using Telligent.Common.Diagnostics.Tracing;

namespace AlexCrome.Telligent.Azure.Filestorage
{

    //TODO: IPersistentUrlGeneratingFileStorageProvider, IEventEnabledCentralizedFileStorageProvider, IHttpAsyncRenderableCentralizedFileStorageProvider 
    public class AzureBlobFilestorageProvider : ICentralizedFileStorageProvider
    {
        private CloudBlobContainer _container;

        public string FileStoreKey { get; private set; }


        public void Initialize(string fileStoreKey, XmlNode configurationNode)
        {
            FileStoreKey = fileStoreKey;

            //TODO: share account & client across provider instances to reuse the buffer pool
            var account = CloudStorageAccount.DevelopmentStorageAccount;
            var client = account.CreateCloudBlobClient();
            _container = client.GetContainerReference(MakeSafeContainerName(fileStoreKey));
            _container.CreateIfNotExists(BlobContainerPublicAccessType.Blob);

            var defaultOptions = client.DefaultRequestOptions;
            defaultOptions.MaximumExecutionTime = TimeSpan.FromSeconds(3);
            defaultOptions.ServerTimeout = TimeSpan.FromSeconds(1);
        }

        public ICentralizedFile AddFile(string path, string fileName, Stream contentStream, bool ensureUniqueFileName)
        {
            if (ensureUniqueFileName)
                fileName = CentralizedFileStorage.GetUniqueFileName(this, path, fileName);

            return AddUpdateFile(path, fileName, contentStream);
        }

        public void AddPath(string path)
        {
            //Azure doesn't support creating a directory without content

            //AddUpdateFile(path, ".placeholder", Stream.Null);
        }

        public ICentralizedFile AddUpdateFile(string path, string fileName, Stream contentStream)
        {
            using (new TracePoint($"[cfs] AddUpdate '{FileStoreKey}' '{path}' '{fileName}'"))
            {
                var blob = GetBlob(path, fileName);

                blob.UploadFromStream(contentStream);

                return new AzureBlobFileReference(blob, FileStoreKey);
            }
        }

        public void Delete() => Delete(string.Empty);

        public void Delete(string path)
        {
            using (new TracePoint($"[cfs] Delete '{FileStoreKey}' '{path}'"))
            {
                foreach (var blob in ListChildren(path, PathSearchOption.AllPaths).OfType<CloudBlob>())
                {
                    blob.DeleteIfExists();
                }
            }
        }

        public void Delete(string path, string fileName)
        {
            using (new TracePoint($"[cfs] Delete '{FileStoreKey}' '{path}' '{fileName}'"))
            {
                var blob = GetBlob(path, fileName);
                blob.DeleteIfExists();
            }
        }

        public ICentralizedFile GetFile(string path, string fileName)
        {
            using (new TracePoint($"[cfs] GetFile '{FileStoreKey}' '{path}' '{fileName}'"))
            {
                var blob = GetBlob(path, fileName);

                if (blob.Exists())
                    return new AzureBlobFileReference(blob, FileStoreKey);

                return null;
            }
        }

        public IEnumerable<ICentralizedFile> GetFiles(PathSearchOption searchOption)
            => GetFiles(string.Empty, searchOption);

        public IEnumerable<ICentralizedFile> GetFiles(string path, PathSearchOption searchOption)
        {
            using (new TracePoint($"[cfs] GetFiles '{FileStoreKey}' '{path}'"))
            {
                return ListChildren(path, searchOption)
                .OfType<CloudBlockBlob>()
                .Select(x => new AzureBlobFileReference(x, FileStoreKey));
            }
        }


        public IEnumerable<string> GetPaths()
            => GetPaths(string.Empty);

        public IEnumerable<string> GetPaths(string path)
        {
            using (new TracePoint($"[cfs] GetPaths '{FileStoreKey}' '{path}'"))
            {
                //TODO: Not entirely sure this is correct
                return ListChildren(path, PathSearchOption.TopLevelPathOnly)
                .OfType<CloudBlobDirectory>()
                .Select(x => x.Prefix);
            }
        }


        public static string MakeSafeContainerName(string fileStoreKey)
        {
            var containerName = fileStoreKey.Replace("-", "--")
                                .Replace(".", "-");
            return WebUtility.UrlEncode(containerName);
        }

        private CloudBlockBlob GetBlob(string path, string fileName)
        {
            var azurePath = ConvertCfsPathToAzurePath(path);
            return _container.GetBlockBlobReference(azurePath + "/" + fileName);
        }

        private IEnumerable<IListBlobItem> ListChildren(string path, PathSearchOption searchOption)
        {
            bool includeSubDirectories = searchOption == PathSearchOption.AllPaths;

            IEnumerable<IListBlobItem> blobs;

            if (string.IsNullOrEmpty(path))
            {
                blobs = _container.ListBlobs(useFlatBlobListing: includeSubDirectories);
            }
            else
            {
                var azurePath = ConvertCfsPathToAzurePath(path);
                blobs = _container.GetDirectoryReference(azurePath)
                                .ListBlobs(includeSubDirectories);
            }

            return new NotFoundHandlingEnumerable(blobs);
        }



        private string ConvertCfsPathToAzurePath(string cfsPath)
            => cfsPath?.Replace('.', '/').Trim('.');


        private class NotFoundHandlingEnumerable : IEnumerable<IListBlobItem>
        {
            private readonly IEnumerable<IListBlobItem> _original;
            public NotFoundHandlingEnumerable(IEnumerable<IListBlobItem> original)
            {
                _original = original;
            }

            public IEnumerator<IListBlobItem> GetEnumerator() => new NotFoundHandlingEnumerator(_original.GetEnumerator());

            IEnumerator IEnumerable.GetEnumerator() => new NotFoundHandlingEnumerator(_original.GetEnumerator());

            private class NotFoundHandlingEnumerator : IEnumerator<IListBlobItem>
            {
                private IEnumerator<IListBlobItem> _original;
                public NotFoundHandlingEnumerator(IEnumerator<IListBlobItem> original)
                {
                    _original = original;
                }

                public object Current => _original.Current;

                IListBlobItem IEnumerator<IListBlobItem>.Current => _original.Current;

                public void Dispose() => _original.Dispose();
                public bool MoveNext()
                {
                    try
                    {
                        return _original.MoveNext();
                    }
                    catch (StorageException ex) when (ex.RequestInformation.HttpStatusCode == 404)
                    {
                        return false;
                    }
                }

                public void Reset() => _original.Reset();
            }
        }


    }
}
