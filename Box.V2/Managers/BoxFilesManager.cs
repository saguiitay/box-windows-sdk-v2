﻿using Box.V2.Auth;
using Box.V2.Contracts;
using Box.V2.Models;
using Box.V2.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Box.V2.Managers
{
    /// <summary>
    /// File objects represent that metadata about individual files in Box, with attributes describing who created the file, 
    /// when it was last modified, and other information. 
    /// </summary>
    public class BoxFilesManager : BoxResourceManager
    {
        public BoxFilesManager(IBoxConfig config, IBoxService service, IBoxConverter converter, IAuthRepository auth)
            : base(config, service, converter, auth) { }

        /// <summary>
        /// Gets a file object representation of the provided file Id
        /// </summary>
        /// <param name="id">Id of file information to retrieve</param>
        /// <param name="limit">The number of items to return (default=100, max=1000)</param>
        /// <param name="offset">The item at which to begin the response (default=0)</param>
        /// <returns></returns>
        public async Task<BoxFile> GetInformationAsync(string id)
        {
            CheckPrerequisite(id);

            BoxRequest request = new BoxRequest(_config.FilesEndpointUri, id)
                .Authorize(_auth.Session.AccessToken);

            IBoxResponse<BoxFile> response = await ToResponseAsync<BoxFile>(request);

            return response.ResponseObject;
        }

        /// <summary>
        /// Returns the stream of the requested file
        /// </summary>
        /// <param name="id">Id of the file to download</param>
        /// <returns>MemoryStream of the requested file</returns>
        public async Task<Stream> DownloadStreamAsync(string id)
        {
            CheckPrerequisite(id);

            BoxRequest request = new BoxRequest(_config.FilesEndpointUri, string.Format(Constants.ContentPathString, id))
                .Authorize(_auth.Session.AccessToken);

            IBoxResponse<Stream> response = await ToResponseAsync<Stream>(request);

            return response.ResponseObject;
        }

        /// <summary>
        /// Uploads a provided file to the target parent folder
        /// If the file already exists, an error will be thrown
        /// </summary>
        /// <param name="fileRequest"></param>
        /// <param name="stream"></param>
        /// <returns></returns>
        public async Task<BoxFile> UploadAsync(BoxFileRequest fileRequest, Stream stream)
        {
            stream.ThrowIfNull("stream");
            CheckPrerequisite(
                fileRequest.ThrowIfNull("fileRequest").Name,
                fileRequest.Parent.ThrowIfNull("fileRequest.Parent").Id);

            BoxMultiPartRequest request = new BoxMultiPartRequest(_config.FilesUploadEndpointUri)
                .Authorize(_auth.Session.AccessToken)
                .FormPart(new BoxStringFormPart()
                {
                    Name = "metadata",
                    Value = _converter.Serialize(fileRequest)
                })
                .FormPart(new BoxFileFormPart()
                {
                    Name = "file",
                    Value = stream,
                    FileName = fileRequest.Name
                });

            IBoxResponse<BoxCollection<BoxFile>> response = await ToResponseAsync<BoxCollection<BoxFile>>(request, true);

            // We can only upload one file at a time, so return the first entry
            return response.ResponseObject.Entries.FirstOrDefault();
        }

        /// <summary>
        /// This method is used to upload a new version of an existing file in a user’s account. Similar to regular file uploads, 
        /// these are performed as multipart form uploads An optional If-Match header can be included to ensure that client only 
        /// overwrites the file if it knows about the latest version. The filename on Box will remain the same as the previous version.
        /// </summary>
        /// <param name="fileName"></param>
        /// <param name="stream"></param>
        /// <param name="etag"></param>
        /// <returns></returns>
        public async Task<BoxFile> UploadNewVersionAsync(string fileName, string fileId, Stream stream, string etag = null)
        {
            stream.ThrowIfNull("stream");
            CheckPrerequisite(etag, fileName);

            BoxMultiPartRequest request = new BoxMultiPartRequest(new Uri(string.Format(Constants.FilesNewVersionEndpointString, fileId)))
                .Header("If-Match", etag)
                .Authorize(_auth.Session.AccessToken)
                .FormPart(new BoxFileFormPart()
                {
                    Name = "filename",
                    Value = stream,
                    FileName = fileName
                });

            IBoxResponse<BoxCollection<BoxFile>> response = await ToResponseAsync<BoxCollection<BoxFile>>(request);

            // We can only upload one file at a time, so return the first entry
            return response.ResponseObject.Entries.FirstOrDefault();
        }

        /// <summary>
        /// If there are previous versions of this file, this method can be used to retrieve metadata about the older versions.
        /// <remarks>Versions are only tracked for Box users with premium accounts.</remarks>
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public async Task<BoxCollection<BoxFile>> ViewVersionsAsync(string id)
        {
            CheckPrerequisite(id);

            BoxRequest request = new BoxRequest(_config.FilesEndpointUri, string.Format(Constants.VersionsPathString, id))
                .Authorize(_auth.Session.AccessToken);

            IBoxResponse<BoxCollection<BoxFile>> response = await ToResponseAsync<BoxCollection<BoxFile>>(request);

            return response.ResponseObject;
        }

        /// <summary>
        /// Used to update individual or multiple fields in the file object, including renaming the file, changing it’s description, 
        /// and creating a shared link for the file. To move a file, change the ID of its parent folder. An optional etag
        /// can be included to ensure that client only updates the file if it knows about the latest version.
        /// </summary>
        /// <param name="fileRequest"></param>
        /// <returns></returns>
        public async Task<BoxFile> UpdateInformationAsync(BoxFileRequest fileRequest, string etag = null)
        {
            CheckPrerequisite(fileRequest.ThrowIfNull("fileRequest").Id);

            BoxRequest request = new BoxRequest(_config.FilesEndpointUri, fileRequest.Id)
                .Method(RequestMethod.PUT)
                .Authorize(_auth.Session.AccessToken)
                .Header("If-Match", etag);
                
            request.Payload = _converter.Serialize(fileRequest);

            IBoxResponse<BoxFile> response = await ToResponseAsync<BoxFile>(request);

            return response.ResponseObject;
        }

        /// <summary>
        /// Discards a file to the trash. The etag of the file can be included as an ‘If-Match’ header to prevent race conditions.
        /// <remarks>Depending on the enterprise settings for this user, the item will either be actually deleted from Box or moved to the trash.</remarks>
        /// </summary>
        /// <param name="id"></param>
        /// <param name="etag"></param>
        /// <returns></returns>
        public async Task<bool> DeleteAsync(string id, string etag)
        {
            CheckPrerequisite(id, etag);

            BoxRequest request = new BoxRequest(_config.FilesUploadEndpointUri, id)
                .Method(RequestMethod.DELETE)
                .Authorize(_auth.Session.AccessToken)
                .Header("If-Match", etag);

            IBoxResponse<BoxFile> response = await ToResponseAsync<BoxFile>(request);

            return response.Status == ResponseStatus.Success;
        }

        /// <summary>
        /// Used to create a copy of a file in another folder. The original version of the file will not be altered.
        /// </summary>
        /// <param name="fileRequest"></param>
        /// <returns></returns>
        public async Task<BoxFile> CopyAsync(BoxFileRequest fileRequest)
        {
            CheckPrerequisite(fileRequest.ThrowIfNull("fileRequest").Name,
                fileRequest.Parent.ThrowIfNull("fileRequest.Parent").Id);

            BoxRequest request = new BoxRequest(_config.FilesUploadEndpointUri, string.Format(Constants.CopyPathString, fileRequest.Id))
                .Method(RequestMethod.POST)
                .Authorize(_auth.Session.AccessToken);
            request.Payload = _converter.Serialize(fileRequest);

            IBoxResponse<BoxFile> response = await ToResponseAsync<BoxFile>(request);

            return response.ResponseObject;
        }

        /// <summary>
        /// Used to create a shared link for this particular file. Please see here for more information on the permissions available for shared links. 
        /// </summary>
        /// <param name="id"></param>
        /// <param name="sharedLinkRequest"></param>
        /// <returns></returns>
        public async Task<BoxFile> CreateSharedLinkAsync(string id, BoxSharedLinkRequest sharedLinkRequest)
        {
            CheckPrerequisite(id);
            if (!sharedLinkRequest.ThrowIfNull("sharedLinkRequest").Access.HasValue)
                throw new ArgumentException("A required field is missing", "sharedLink.Access");

            BoxRequest request = new BoxRequest(_config.FilesUploadEndpointUri, id)
                .Method(RequestMethod.POST)
                .Authorize(_auth.Session.AccessToken);
            request.Payload = _converter.Serialize(new BoxItemRequest() { SharedLink = sharedLinkRequest });

            IBoxResponse<BoxFile> response = await ToResponseAsync<BoxFile>(request);

            return response.ResponseObject;
        }

        /// <summary>
        /// Retrieves the comments on a particular file, if any exist.
        /// </summary>
        /// <param name="id">The Id of the item the comments should be retrieved for</param>
        /// <returns>A Collection of comment objects are returned. If there are no comments on the file, an empty comments array is returned</returns>
        public async Task<BoxCollection<BoxComment>> GetCommentsAsync(string id)
        {
            CheckPrerequisite(id);

            BoxRequest request = new BoxRequest(_config.FilesUploadEndpointUri, string.Format(Constants.CommentsPathString, id))
                .Authorize(_auth.Session.AccessToken);

            IBoxResponse<BoxCollection<BoxComment>> response = await ToResponseAsync<BoxCollection<BoxComment>>(request);

            return response.ResponseObject;
        }

        /// <summary>
        /// Retrieves a thumbnail, or smaller image representation, of this file. Sizes of 32x32, 64x64, 128x128, and 256x256 can be returned. 
        /// Currently thumbnails are only available in .png format and will only be generated for
        /// <see cref="http://en.wikipedia.org/wiki/Image_file_formats"/>
        /// </summary>
        /// <param name="id"></param>
        /// <param name="minHeight"></param>
        /// <param name="minWidth"></param>
        /// <param name="maxHeight"></param>
        /// <param name="maxWidth"></param>
        /// <returns></returns>
        public async Task<Stream> GetThumbnailAsync(string id, int? minHeight = null, int? minWidth = null, int? maxHeight = null, int? maxWidth = null)
        {
            CheckPrerequisite(id);

            BoxRequest request = new BoxRequest(_config.FilesUploadEndpointUri, string.Format(Constants.ThumbnailPathString, id))
                .Authorize(_auth.Session.AccessToken)
                .Param("min_height", minHeight.ToString())
                .Param("min_width", minWidth.ToString())
                .Param("max_height", maxHeight.ToString())
                .Param("max_width", maxWidth.ToString());

            IBoxResponse<Stream> response = await ToResponseAsync<Stream>(request);

            return response.ResponseObject;
        }

        /// <summary>
        /// Gets the stream of a preview page
        /// </summary>
        /// <param name="id"></param>
        /// <param name="page"></param>
        /// <returns>A PNG of the preview</returns>
        public async Task<Stream> GetPreviewAsync(string id, int page)
        {
            CheckPrerequisite(id);

            BoxRequest request = new BoxRequest(new Uri(string.Format("https://www.box.net/api/2.0/files/{0}/preview.png", id)))
                .Authorize(_auth.Session.AccessToken)
                .Param("page", page.ToString());

            IBoxResponse<Stream> response = await ToResponseAsync<Stream>(request);

            return response.ResponseObject;
            
        }

        /// <summary>
        /// Retrieves an item that has been moved to the trash.
        /// </summary>
        /// <param name="id"></param>
        /// <returns>The full item will be returned, including information about when the it was moved to the trash.</returns>
        public async Task<BoxFile> GetTrashedAsync(string id)
        {
            CheckPrerequisite(id);

            BoxRequest request = new BoxRequest(_config.FilesUploadEndpointUri, string.Format(Constants.TrashPathString, id))
                .Authorize(_auth.Session.AccessToken);

            IBoxResponse<BoxFile> response = await ToResponseAsync<BoxFile>(request);

            return response.ResponseObject;
        }

        /// <summary>
        /// Restores an item that has been moved to the trash. Default behavior is to restore the item to the folder it was in before 
        /// it was moved to the trash. If that parent folder no longer exists or if there is now an item with the same name in that 
        /// parent folder, the new parent folder and/or new name will need to be included in the request.
        /// </summary>
        /// <returns>The full item will be returned with a 201 Created status. By default it is restored to the parent folder it was in before it was trashed.</returns>
        public async Task<BoxFile> RestoreTrashedAsync(BoxFileRequest fileRequest)
        {
            CheckPrerequisite(fileRequest.ThrowIfNull("fileRequest").Id,
                fileRequest.Name);

            BoxRequest request = new BoxRequest(_config.FilesUploadEndpointUri, fileRequest.Id)
                .Authorize(_auth.Session.AccessToken)
                .Method(RequestMethod.POST);
            request.Payload = _converter.Serialize(fileRequest);

            IBoxResponse<BoxFile> response = await ToResponseAsync<BoxFile>(request);

            return response.ResponseObject;
        }

        /// <summary>
        /// Permanently deletes an item that is in the trash. The item will no longer exist in Box. This action cannot be undone.
        /// </summary>
        /// <param name="id"></param>
        /// <returns>An empty 204 No Content response will be returned upon successful deletion</returns>
        public async Task<bool> PurgeTrashedAsync(string id)
        {
            CheckPrerequisite(id);

            BoxRequest request = new BoxRequest(_config.FilesUploadEndpointUri, string.Format(Constants.TrashPathString, id))
                .Method(RequestMethod.DELETE)
                .Authorize(_auth.Session.AccessToken);

            IBoxResponse<BoxFile> response = await ToResponseAsync<BoxFile>(request);

            return response.Status == ResponseStatus.Success;
        }

        /*** Not used

        /// <summary>
        /// Returns the byte array of the requested file
        /// </summary>
        /// <param name="id">Id of the file to download</param>
        /// <returns>byte[] of the requested file</returns>
        public async Task<byte[]> DownloadBytesAsync(string id)
        {
            CheckPrerequisite(id);

            BoxRequest request = new BoxRequest(_config.FilesEndpointUri, string.Format(Constants.ContentPathString, id))
                .Authorize(_auth.Session.AccessToken);

            IBoxResponse<byte[]> response = await ToResponseAsync<byte[]>(request, true);

            return response.ResponseObject;
        } 
        
        
        public async Task<BoxFile> UploadAsync(BoxFileRequest fileRequest, byte[] file)
        {

            file.ThrowIfNull("file");
            CheckPrerequisite(
                fileRequest.ThrowIfNull("fileRequest").Name,
                fileRequest.Parent.ThrowIfNull("fileRequest.Parent").Id);

            BoxMultiPartRequest request = new BoxMultiPartRequest(_config.FilesUploadEndpointUri)
                .Authorize(_auth.Session.AccessToken)
                .FormPart(new BoxStringFormPart()
                {
                    Name = "metadata",
                    Value = _converter.Serialize(fileRequest)
                });

            IBoxResponse<BoxFile> response = await ToResponseAsync<BoxFile>(request, true);

            return response.ResponseObject;
        }
         ***/
    }
}