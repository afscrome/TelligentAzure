using Microsoft.WindowsAzure.Storage.Blob;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Telligent.Evolution.Extensibility.Storage.Version1;

namespace AlexCrome.Telligent.Azure.Filestorage
{
    public class BlobRequestProcessor
    {
        private readonly CloudBlob _blob;
        public BlobRequestProcessor(CloudBlob blob)
        {
            _blob = blob;
        }

        public async Task ProcessRequest(HttpContextBase context, AzureBlobFileReference file)
        {
            var response = context.Response;
            if (!await _blob.ExistsAsync())
            {
                response.StatusCode = 404;
                return;
            }




        }

        private void NotFoundResponse(AzureBlobFileReference file, HttpResponse response)
        {
            response.Cache.SetCacheability(GetCacheability(file));
        }

        private void FoundHeadResponse(AzureBlobFileReference file, HttpResponse response)
        {
            response.Cache.SetCacheability(GetCacheability(file));
            response.Cache.SetOmitVaryStar(true);
            response.StatusCode = (int)System.Net.HttpStatusCode.OK;
        }

        HttpCacheability GetCacheability (AzureBlobFileReference file)
        {
            return CentralizedFileStorage.AccessValidationIsGlobal(file.FileStoreKey)
                ? HttpCacheability.Public
                : HttpCacheability.Private;
        }

        private bool CheckPreconditions(HttpRequestBase request)
        {
            request
        }


        private enum PreconditionState
        {
            Unspecified,
            Met,
            Failed,
            NotModified
        }

    }
}
