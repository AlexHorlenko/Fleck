using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Fleck
{
    class ExtensionNegotiationFailureException : Exception
    {
        public ExtensionNegotiationFailureException() : base() { }
        
        public ExtensionNegotiationFailureException(string message) : base(message) {}

        public ExtensionNegotiationFailureException(string message, Exception innerException) : base(message, innerException) { }
    }
}
