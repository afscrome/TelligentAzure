using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Telligent.Evolution.Extensibility.Storage.Version1;
using System;
using System.Collections;
using System.Configuration;
using System.Net;
using Telligent.Common.Diagnostics.Tracing;
using System.Threading.Tasks;
using System.Web;
using Telligent.Evolution.Extensibility.Version1;
using Microsoft.WindowsAzure.Storage.Shared.Protocol;

namespace AlexCrome.Telligent.Azure.Filestorage
{

    //TODO: IPersistentUrlGeneratingFileStorageProvider, IEventEnabledCentralizedFileStorageProvider, IHttpAsyncRenderableCentralizedFileStorageProvider 
    public class AzureBlobFilestorageProvider : ICentralizedFileStorageProvider//, IHttpAsyncRenderableCentralizedFileStorageProvider, IPersistentUrlGeneratingFileStorageProvider
    {
        private CloudBlobContainer _container;
        private FileStoreData _fileStoreData;
        public string FileStoreKey => _fileStoreData.FileStoreKey;

        public void Initialize(string fileStoreKey, XmlNode configurationNode)
        {
            _fileStoreData = new FileStoreData(fileStoreKey, IsPublic(fileStoreKey));
            _container = CreateContainer();
        }

        private bool IsPublic(string fileStoreKey)
        {
            var fileStorePlugin = PluginManager.Get<ICentralizedFileStore>()
                .Single(x => x.FileStoreKey.Equals(fileStoreKey, StringComparison.OrdinalIgnoreCase));

            return !(fileStorePlugin is ISecuredCentralizedFileStore || fileStorePlugin is IGloballySecuredCentralizedFileStore);
        }

        private CloudBlobContainer CreateContainer()
        {
            //TODO: share account & client across provider instances to reuse the buffer pool
            var account = CloudStorageAccount.Parse(ConfigurationManager.ConnectionStrings["AzureFilestorageContainer"].ConnectionString);
            var client = account.CreateCloudBlobClient();

            var defaultOptions = client.DefaultRequestOptions;
            defaultOptions.MaximumExecutionTime = TimeSpan.FromSeconds(3);
            defaultOptions.ServerTimeout = TimeSpan.FromSeconds(1);

            var container = client.GetContainerReference(MakeSafeContainerName(FileStoreKey)); ;
            SecureContainer(container);
            return container;
        }

        private void SecureContainer(CloudBlobContainer container)
        {
            var accessType = _fileStoreData.IsPublic
                ? BlobContainerPublicAccessType.Blob
                : BlobContainerPublicAccessType.Off;

            //If the container is created, don't need to set permissions a second time
            if (container.CreateIfNotExists(accessType))
                return;

            var permissions = new BlobContainerPermissions
            {
                PublicAccess = accessType,
            };
            container.SetPermissions(permissions);
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
                //Work around for https://community.telligent.com/community/f/1964/t/1141418
                if (contentStream.Position != 0)
                {
                    if (!contentStream.CanSeek)
                        throw new NotSupportedException("Stream is not at beginning, and cannot be seeked to the beginning");

                    contentStream.Seek(0, SeekOrigin.Begin);
                }

                var blob = GetBlob(path, fileName);
                blob.Properties.ContentType = MimeMapping.GetMimeMapping(fileName);

                blob.UploadFromStream(contentStream);

                return new AzureBlobFileReference(blob, _fileStoreData);
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
                    return new AzureBlobFileReference(blob, _fileStoreData);

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
                .Select(x => new AzureBlobFileReference(x, _fileStoreData));
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
