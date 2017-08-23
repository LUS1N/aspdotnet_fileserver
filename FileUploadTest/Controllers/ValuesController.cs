using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;
using System.Web.Routing;

namespace FileUploadTest.Controllers
{
    public class ValuesController : ApiController
    {
        private static string ROOT = "~/storage";

        [HttpGet]
        [Route("api/values")]
        public HttpResponseMessage GetRootFile(string file = null)
        {
            // can't match the root dir without this method
            return GetContent(string.Empty, string.IsNullOrEmpty(file) ? string.Empty : file);
        }

        [HttpGet]
        [Route("api/values/{*path}")]
        public HttpResponseMessage GetContent(string path = null, string file = null)
        {
            // {*path} means that everything after the last '/' will be passed as path
            // including any '/'s

            // add dir path if there's any
            path = string.IsNullOrEmpty(path) ? string.Empty : $"/{path}";

            // add filename if there's one
            path = string.IsNullOrEmpty(file) ? path : $"{path}/{file}";

            // create a full path to the dir or file
            var localPath = HttpContext.Current.Server.MapPath($"{ROOT}{path}");

            // if file name wasn't passed it's a dir else a file
            return string.IsNullOrEmpty(file) ? GetDirectoryContents(localPath) : GetFile(localPath);
        }

        [HttpDelete]
        [Route("api/values")]
        public HttpResponseMessage DeleteContent(string file = null)
        {
            var path = HttpContext.Current.Server.MapPath($"{ROOT}/{file}");

            var fileInfo = new FileInfo(path);

            if (string.IsNullOrEmpty(file) || !fileInfo.Exists) return Request.CreateResponse(HttpStatusCode.NotFound);

            fileInfo.Delete();
            return Request.CreateResponse(HttpStatusCode.OK);
        }

        [HttpDelete]
        [Route("api/values/{*path}")]
        public HttpResponseMessage DeleteNestedContent(string path, string file = null)
        {
            // dir path
            var fullDirPath = HttpContext.Current.Server.MapPath($"{ROOT}/{path}");
            // path with filename
            var fullPath = string.IsNullOrEmpty(file) ? fullDirPath : $"{fullDirPath}/{file}";

            // if file is provided - delete it
            if (string.IsNullOrEmpty(file))
                return Request.CreateResponse(DeleteDirectory(fullPath) ? HttpStatusCode.OK : HttpStatusCode.NotFound);

            // if no path provided return 404, to avoid deleting the whole root
            if (string.IsNullOrEmpty(path))
                Request.CreateResponse(HttpStatusCode.NotFound);

            return Request.CreateResponse(DeleteFile(fullPath) ? HttpStatusCode.OK : HttpStatusCode.NotFound);
        }

        [HttpPost]
        [Route("api/values")]
        public HttpResponseMessage CreateFileOrDir()
        {
            return CreateFileOrDirNested();
        }

        [HttpPost]
        [Route("api/values/{*path}")]
        public HttpResponseMessage CreateFileOrDirNested(string path = null)
        {
            var httpRequest = HttpContext.Current.Request;
            var fullServerPath = HttpContext.Current.Server.MapPath(path != null ? $"{ROOT}/{path}" : ROOT);

            if (httpRequest.Files.Count > 0)
            {
                return SaveFiles(path, httpRequest, fullServerPath);
            }

            // if dir exists return conflict
            if (Directory.Exists(fullServerPath))
                return Request.CreateResponse(HttpStatusCode.Conflict);

            Directory.CreateDirectory(fullServerPath);
            return Request.CreateResponse(HttpStatusCode.Created);
        }


        private HttpResponseMessage GetFile(string localPath)
        {
            // if file doesn't exist return 404
            if (!File.Exists(localPath))
                return Request.CreateResponse(HttpStatusCode.NotFound);

            var result = Request.CreateResponse(HttpStatusCode.OK);

            // add the file to the response
            result.Content = new StreamContent(new FileStream(localPath, FileMode.Open, FileAccess.Read));

            // split the path into directories
            var splitPath = localPath.Split('\\');

            // grap the last part which is the file name
            var fileName = splitPath[splitPath.Length - 1];

            // attach the filename which the user will see and return
            result.Content.Headers.ContentDisposition =
                new System.Net.Http.Headers.ContentDispositionHeaderValue("attachment") { FileName = fileName };
            return result;
        }

        private HttpResponseMessage GetDirectoryContents(string localPath)
        {
            if (!Directory.Exists(localPath))
                return Request.CreateResponse(HttpStatusCode.NotFound);

            // will return both files and directories
            var contents = new List<dynamic>();

            // the requested dir
            var directory = new DirectoryInfo(localPath);

            AddAllFiles(directory.GetFiles("*.*", SearchOption.TopDirectoryOnly), contents);
            AddAllDirectories(directory.GetDirectories(), contents);

            // sort by name
            contents = contents.OrderBy(o => o.Name).ToList();
            return Request.CreateResponse(HttpStatusCode.OK, contents);
        }

        private static void AddAllDirectories(IEnumerable<DirectoryInfo> dirs, ICollection<dynamic> contents)
        {
            foreach (var dir in dirs)
            {
                contents.Add(new
                {
                    Name = $"{dir.Name}/",
                    IsFile = false
                });
            }
        }

        private static void AddAllFiles(IEnumerable<FileInfo> fileList, ICollection<dynamic> contents)
        {
            foreach (var file in fileList)
            {
                contents.Add(new
                {
                    file.Name,
                    Size = SizeSuffix(file.Length),
                    IsFile = true
                });
            }
        }

        private bool DeleteFile(string fullPath)
        {
            if (!File.Exists(fullPath))
                return false;

            var file = new FileInfo(fullPath);
            file.Delete();
            return true;
        }

        private bool DeleteDirectory(string fullPath)
        {
            if (!Directory.Exists(fullPath)) return false;

            SetAttributesNormal(new DirectoryInfo(fullPath));
            Directory.Delete(fullPath, true);
            return true;
        }

        private void SetAttributesNormal(DirectoryInfo dir)
        {
            dir.Attributes = FileAttributes.Normal;
            foreach (var subDir in dir.GetDirectories())
            {
                SetAttributesNormal(subDir);
                subDir.Attributes = FileAttributes.Normal;
            }
            foreach (var file in dir.GetFiles())
            {
                file.Attributes = FileAttributes.Normal;
            }
        }


        

        private HttpResponseMessage SaveFiles(string path, HttpRequest httpRequest, string fullServerPath)
        {
            var docfiles = new List<string>();
            foreach (var file in httpRequest.Files.GetMultiple("files"))
            {
                // replace spaces with underscore if there are any, so it's url accesible
                var fileName = file.FileName.Replace(' ', '_');

                var fullPathWFile = $"{fullServerPath}/{fileName}";

                try
                {
                    file.SaveAs(fullPathWFile);
                }
                catch (DirectoryNotFoundException)
                {
                    return Request.CreateResponse(HttpStatusCode.BadRequest, $"Directory {path} not found.");
                }

                docfiles.Add($"{path}/{fileName}");
            }
            return Request.CreateResponse(HttpStatusCode.Created, docfiles);
        }

        static readonly string[] SizeSuffixes =
            { "bytes", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB" };

        static string SizeSuffix(long value, int decimalPlaces = 1)
        {
            if (value < 0) { return "-" + SizeSuffix(-value); }
            if (value == 0) { return "0.0 bytes"; }

            // mag is 0 for bytes, 1 for KB, 2, for MB, etc.
            var mag = (int)Math.Log(value, 1024);

            // 1L << (mag * 10) == 2 ^ (10 * mag) 
            // [i.e. the number of bytes in the unit corresponding to mag]
            var adjustedSize = (decimal)value / (1L << (mag * 10));

            // make adjustment when the value is large enough that
            // it would round up to 1000 or more
            if (Math.Round(adjustedSize, decimalPlaces) >= 1000)
            {
                mag += 1;
                adjustedSize /= 1024;
            }

            return string.Format("{0:n" + decimalPlaces + "} {1}",
                adjustedSize,
                SizeSuffixes[mag]);
        }
    }
}
