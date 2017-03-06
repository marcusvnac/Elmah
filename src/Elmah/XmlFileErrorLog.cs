#region License, Terms and Author(s)
//
// ELMAH - Error Logging Modules and Handlers for ASP.NET
// Copyright (c) 2004-9 Atif Aziz. All rights reserved.
//
//  Author(s):
//
//      Scott Wilson <sw@scratchstudio.net>
//      Atif Aziz, http://www.raboof.com
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//    http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
#endregion

[assembly: Elmah.Scc("$Id: XmlFileErrorLog.cs 795 2011-02-16 22:29:34Z azizatif $")]

namespace Elmah
{
    #region Imports

    using System;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Text;
    using System.Xml;
    using System.Collections.Generic;

    using IDictionary = System.Collections.IDictionary;
    using System.Security;

    #endregion

    /// <summary>
    /// An <see cref="ErrorLog"/> implementation that uses XML files stored on 
    /// disk as its backing store.
    /// </summary>

    public class XmlFileErrorLog : ErrorLog
    {
        private readonly string _logPath;
        private readonly int _fileListSize;

        /// <summary>
        /// Initializes a new instance of the <see cref="XmlFileErrorLog"/> class
        /// using a dictionary of configured settings.
        /// </summary>

        public XmlFileErrorLog(IDictionary config)
        {
            if (config == null) throw new ArgumentNullException("config");

            var logPath = config.Find("logPath", string.Empty);

            if (logPath.Length == 0)
            {
                //
                // For compatibility reasons with older version of this
                // implementation, we also try "LogPath".
                //

                logPath = config.Find("LogPath", string.Empty);

                if (logPath.Length == 0)
                    throw new ApplicationException("Log path is missing for the XML file-based error log.");
            }

            if (logPath.StartsWith("~/"))
                logPath = MapPath(logPath);

            _logPath = logPath;

            var fileListSizeString = config.Find("size", string.Empty);

            if (fileListSizeString.Length > 0)
                _fileListSize = Convert.ToInt32(fileListSizeString, CultureInfo.InvariantCulture);
        }

        /// <remarks>
        /// This method is excluded from inlining so that if 
        /// HostingEnvironment does not need JIT-ing if it is not implicated
        /// by the caller.
        /// </remarks>

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string MapPath(string path)
        {
            return System.Web.Hosting.HostingEnvironment.MapPath(path);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="XmlFileErrorLog"/> class
        /// to use a specific path to store/load XML files.
        /// </summary>

        public XmlFileErrorLog(string logPath)
        {
            if (logPath == null) throw new ArgumentNullException("logPath");
            if (logPath.Length == 0) throw new ArgumentException(null, "logPath");
            _logPath = logPath;
        }

        /// <summary>
        /// Gets the path to where the log is stored.
        /// </summary>

        public virtual string LogPath
        {
            get { return _logPath; }
        }

        /// <summary>
        /// Gets the name of this error log implementation.
        /// </summary>

        public override string Name
        {
            get { return "XML File-Based Error Log"; }
        }

        /// <summary>
        /// Logs an error to the database.
        /// </summary>
        /// <remarks>
        /// Logs an error as a single XML file stored in a folder. XML files are named with a
        /// sortable date and a unique identifier. If the size attribute is set in the configuration
        /// then this will remove older log files as new files are created.
        /// </remarks>

        public override string Log(Error error)
        {
            var logPath = LogPath;
            if (!Directory.Exists(logPath))
                Directory.CreateDirectory(logPath);

            var errorId = Guid.NewGuid().ToString();

            var timeStamp = (error.Time > DateTime.MinValue ? error.Time : DateTime.Now);

            var fileName = string.Format(CultureInfo.InvariantCulture,
                                  @"error-{0:yyyy-MM-ddHHmmssZ}-{1}.xml",
                                  /* 0 */ timeStamp.ToUniversalTime(),
                                  /* 1 */ errorId);

            var path = Path.Combine(logPath, fileName);

            using (var writer = new XmlTextWriter(path, Encoding.UTF8))
            {
                writer.Formatting = Formatting.Indented;
                writer.WriteStartElement("error");
                writer.WriteAttributeString("errorId", errorId);
                ErrorXml.Encode(error, writer);
                writer.WriteEndElement();
                writer.Flush();
            }

            // Do not try to delete files if the current exception was caused by a previous
            // attempt to delete old files.
            if (!IsXmlFileErrorLogDeleteException(error.Exception))
            {
                DeleteOldFiles();
            }

            return errorId;
        }

        /// <summary>
        /// Check if the exception or its InnerException is of type XmlFileErrorLogDeleteException.
        /// InnerException is checked in in case the XmlFileErrorLogDeleteException is caught and wrapped
        /// by some other code up the call stack.
        /// </summary>
        /// <param name="ex">Exception to test</param>
        /// <returns>True if the exception or its InnerExcpetion if of type 
        /// XmlFileErrorLogDeleteException, otherwise false.</returns>
        private bool IsXmlFileErrorLogDeleteException(Exception ex)
        {
            if (ex == null) return false;

            if (ex.GetType() == typeof(XmlFileErrorLogDeleteException)) return true;

            return IsXmlFileErrorLogDeleteException(ex.InnerException);
        }

        /// <summary>
        /// Deletes Old files according the <code>size</code> parameter
        /// </summary>
        /// <remarks>Exceptions are recorded directly to the XmlFileErrorLog.</remarks>
        private void DeleteOldFiles()
        {
            if (_fileListSize == 0)
                return;

            string ErrorLogMessage = "Error during cleaning old log files for the XML file-based error log.";

            try
            {
                var dir = new DirectoryInfo(LogPath);
                if (dir.Exists)
                {
                    var infos = dir.GetFiles("error-*.xml");
                    if (infos.Any())
                    {

                        var files = infos.Where(info => IsUserFile(info.Attributes))
                                         .OrderByDescending(f => f.CreationTime).Skip(_fileListSize);

                        foreach (var file in files)
                            file.Delete();
                    }
                }
            }
            catch (Exception ex)
            {
                var messageFormat = "{0} {1}";
                if (ex.GetType() == typeof(IOException))
                {
                    messageFormat = "{0} The target file is open or memory-mapped or there is an open handle on the file.";
                }
                else if (ex.GetType() == typeof(SecurityException) || ex.GetType() == typeof(UnauthorizedAccessException))
                {
                    messageFormat = "{0} Elmah does not have permission to delete old files.";
                }

                Log(new Error(
                        new XmlFileErrorLogDeleteException(string.Format(messageFormat, ErrorLogMessage, ex.Message), ex),
                        new System.Web.HttpContextWrapper(System.Web.HttpContext.Current)));
            }
        }


        /// <summary>
        /// Returns a page of errors from the folder in descending order 
        /// of logged time as defined by the sortable filenames.
        /// </summary>

        public override int GetErrors(int pageIndex, int pageSize, ICollection<ErrorLogEntry> errorEntryList)
        {
            if (pageIndex < 0) throw new ArgumentOutOfRangeException("pageIndex", pageIndex, null);
            if (pageSize < 0) throw new ArgumentOutOfRangeException("pageSize", pageSize, null);

            var logPath = LogPath;
            var dir = new DirectoryInfo(logPath);
            if (!dir.Exists)
                return 0;

            var infos = dir.GetFiles("error-*.xml");
            if (!infos.Any())
                return 0;

            var files = infos.Where(info => IsUserFile(info.Attributes))
                             .OrderBy(info => info.Name, StringComparer.OrdinalIgnoreCase)
                             .Select(info => Path.Combine(logPath, info.Name))
                             .Reverse()
                             .ToArray();

            if (errorEntryList != null)
            {
                var entries = files.Skip(pageIndex * pageSize)
                                   .Take(pageSize)
                                   .Select(LoadErrorLogEntry);

                foreach (var entry in entries)
                    errorEntryList.Add(entry);
            }

            return files.Length; // Return total
        }

        private ErrorLogEntry LoadErrorLogEntry(string path)
        {
            using (var reader = XmlReader.Create(path))
            {
                if (!reader.IsStartElement("error"))
                    return null;

                var id = reader.GetAttribute("errorId");
                var error = ErrorXml.Decode(reader);
                return new ErrorLogEntry(this, id, error);
            }
        }

        /// <summary>
        /// Returns the specified error from the filesystem, or throws an exception if it does not exist.
        /// </summary>

        public override ErrorLogEntry GetError(string id)
        {
            try
            {
                id = (new Guid(id)).ToString(); // validate GUID
            }
            catch (FormatException e)
            {
                throw new ArgumentException(e.Message, id, e);
            }

            var file = new DirectoryInfo(LogPath).GetFiles(string.Format("error-*-{0}.xml", id))
                                                 .FirstOrDefault();

            if (file == null)
                return null;

            if (!IsUserFile(file.Attributes))
                return null;

            using (var reader = XmlReader.Create(file.FullName))
                return new ErrorLogEntry(this, id, ErrorXml.Decode(reader));
        }

        private static bool IsUserFile(FileAttributes attributes)
        {
            return 0 == (attributes & (FileAttributes.Directory |
                                       FileAttributes.Hidden |
                                       FileAttributes.System));
        }
    }
}
