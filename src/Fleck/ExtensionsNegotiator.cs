using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Fleck
{
    public static class ExtensionsNegotiator
    {
        public static WebSocketExtension Negotiate(IEnumerable<WebSocketExtension> serverExtensions, IEnumerable<string> clientOffer)
        {
            var clientExtensions = clientOffer.Select(GetClientWebSocketExtensionWithAttributes);
            if (!serverExtensions.Any() || !clientOffer.Any())
            {
                return null;
            }

            var matches = clientExtensions.Join(serverExtensions, x => x.Name, y => y.Name, (x, y) => y);
            if (!matches.Any())
            {
                throw new ExtensionNegotiationFailureException("Unable to negotiate an extension");
            }
            var result = matches.First();
            return result;
        }

        private static string CleanExtensionAttributes(string extension)
        {
            return new string(extension.TakeWhile(x => x != ';').ToArray()).Trim();
        }

        //TODO: Make new helper class
        //TODO: Make regex to parse extensions with one operation
        private static WebSocketExtension GetClientWebSocketExtensionWithAttributes(string extension)
        {
            var extensionArray = extension.Split(';').Select(x => x.Trim()).ToArray();
            var wsExtension = new WebSocketExtension();
            wsExtension.Name = extensionArray[0];
            for (int i = 1; i < extensionArray.Count(); i++)
            { 
                var extensionAttributeArray = extensionArray[i].Split('=');
                var wsExtensionAttribute = new WebSocketExtensionAttribute();
                wsExtensionAttribute.Name = extensionAttributeArray[0];
                if (extensionAttributeArray.Count()>1)
                    wsExtensionAttribute.Value = extensionAttributeArray[1];
                wsExtension.SupportedAttributes.Add(new Tuple<WebSocketExtensionAttribute, bool>(wsExtensionAttribute, false));
            }
            return wsExtension;
        }

    }
}
