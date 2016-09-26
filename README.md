# Azure Filestorage provider for Telligent Community

This repo is a proof of concept CFS provider for Tellgient Community that allows you to store files in Azure Blob Storage.

Right now, we recommend using the Azure cfs provider from the very begining of your community - migrating content to the azure aftewards can be a bit of a pain (specifically around correctly setting mime types)

To use the cfs provider, build the code, copy it to you community's `bin` directory and register the filestorage provider in `communityserver_override.config`

```xml
<Override xpath="/CommunityServer/CentralizedFileStorage/fileStoreGroup[@name='default']" mode="remove" /> 
<Override xpath="/CommunityServer/CentralizedFileStorage" mode="add" where="end">
<fileStoreGroup name="Azure"
    default="true" 
    type="AlexCrome.Telligent.Azure.Filestorage.AzureBlobFilestorageProvider, AlexCrome.Telligent.Azure.Filestorage"
    />
</Override>  
```

And add the connection string to your blob storage account in `connectionstrings.config`

```xml
<add name="AzureFilestorageContainer" connectionString="UseDevelopmentStorage=true" />
```
