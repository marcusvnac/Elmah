using System;

namespace Elmah
{
    /// <summary>
    /// Custom exception specifically designed for deleting old logs. Using this 
    /// exception type will skip the log delete code, so to prevent loops of 
    /// trying to delete a log, throwing an exceptions, recording a new log and 
    /// then throwing another exception...
    /// </summary>
    internal class XmlFileErrorLogDeleteException : Exception
    {
        public XmlFileErrorLogDeleteException()
        {
        }

        public XmlFileErrorLogDeleteException(string message)
            : base(message)
        {
        }

        public XmlFileErrorLogDeleteException(string message, Exception internalException)
            : base(message, internalException)
        {
        }
    }
}
