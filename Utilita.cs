using System;
using System.Xml;
using System.Globalization;

namespace ServizioSchedulazione
{
    public class Utilita
    {
        public static string? RecuperaValoreDaXml(XmlDocument doc, string nomeNodo)
        {
            try
            {
                XmlNamespaceManager ns = new XmlNamespaceManager(doc.NameTable);
                ns.AddNamespace("ns", "http://schemas.microsoft.com/win/2004/08/events/event");

                XmlNode node = doc.SelectSingleNode($"//ns:Data[@Name='{nomeNodo}']", ns)
                    ?? doc.SelectSingleNode($"//ns:System/ns:{nomeNodo}", ns);

                return node?.InnerText;
            }
            catch { return null; }
        }

        public static string CostruisciXPath(long ultimoRecordId, DateTime da)
        {
            string condizioneEventi = "(EventID=100 or EventID=102 or EventID=111 or EventID=200 or EventID=201 or EventID=329)";

            string condizioneFiltro = ultimoRecordId > 0
                ? $"EventRecordID > {ultimoRecordId}"
                : $"TimeCreated[@SystemTime >= '{da.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)}']";

            return $"*[System[{condizioneEventi} and {condizioneFiltro}]]";
        }
    }
}
