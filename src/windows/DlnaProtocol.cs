using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Xml;
using System.Xml.Linq;

namespace ZipMp3Player;

internal sealed record DlnaEndpoint(IPAddress Address,IPAddress Mask,string Label)
{
    internal static bool Private(IPAddress ip){var b=ip.GetAddressBytes();return b.Length==4&&(b[0]==10||b[0]==192&&b[1]==168||b[0]==172&&b[1]>=16&&b[1]<=31);}
    internal bool Allows(IPAddress peer)
    {
        if(IPAddress.IsLoopback(Address))return IPAddress.IsLoopback(peer)&&peer.AddressFamily==Address.AddressFamily;
        if(!Private(peer))return false;var a=Address.GetAddressBytes();var p=peer.GetAddressBytes();var m=Mask.GetAddressBytes();
        return a.Length==4&&m.Length==4&&Enumerable.Range(0,4).All(i=>(a[i]&m[i])==(p[i]&m[i]));
    }
    internal static IReadOnlyList<DlnaEndpoint> Available()=>NetworkInterface.GetAllNetworkInterfaces()
        .Where(n=>n.OperationalStatus==OperationalStatus.Up&&n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
        .SelectMany(n=>n.GetIPProperties().UnicastAddresses.Where(a=>Private(a.Address)).Select(a=>new DlnaEndpoint(a.Address,a.IPv4Mask,$"{n.Name} — {a.Address}"))).ToArray();
    public override string ToString()=>Label;
}

internal static class DlnaProtocol
{
    internal const string Cd="urn:schemas-upnp-org:service:ContentDirectory:1",Cm="urn:schemas-upnp-org:service:ConnectionManager:1",Device="urn:schemas-upnp-org:device:MediaServer:1";
    internal static readonly XNamespace Soap="http://schemas.xmlsoap.org/soap/envelope/";
    internal static string DeviceXml(string uuid,string name)=>new XDocument(new XElement(XName.Get("root","urn:schemas-upnp-org:device-1-0"),
        new XElement(XName.Get("specVersion","urn:schemas-upnp-org:device-1-0"),new XElement(XName.Get("major","urn:schemas-upnp-org:device-1-0"),1),new XElement(XName.Get("minor","urn:schemas-upnp-org:device-1-0"),0)),
        DeviceElement(uuid,name))).ToString(SaveOptions.DisableFormatting);
    private static XElement DeviceElement(string uuid,string name)
    {
        XNamespace ns="urn:schemas-upnp-org:device-1-0";
        XElement Value(string key,object value)=>new(ns+key,value);
        XElement Service(string type,string id,string path)=>new(ns+"service",Value("serviceType",type),Value("serviceId","urn:upnp-org:serviceId:"+id),Value("SCPDURL","/"+path+"/scpd.xml"),Value("controlURL","/"+path+"/control"),Value("eventSubURL","/"+path+"/event"));
        return new XElement(ns+"device",Value("deviceType",Device),Value("friendlyName",name),Value("manufacturer","Virtual CD Collection Studio"),Value("modelName","Virtual CD Collection Studio"),Value("UDN","uuid:"+uuid),
            new XElement(ns+"serviceList",Service(Cd,"ContentDirectory","cd"),Service(Cm,"ConnectionManager","cm")));
    }
    internal static XDocument ParseXml(string xml)
    {
        using var reader=XmlReader.Create(new StringReader(xml),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=262144});return XDocument.Load(reader);
    }
    internal static string SoapReply(string service,string action,params (string Name,object Value)[] values)
    {
        XNamespace ns=service;return new XElement(Soap+"Envelope",new XAttribute(XNamespace.Xmlns+"s",Soap),new XAttribute(Soap+"encodingStyle","http://schemas.xmlsoap.org/soap/encoding/"),
            new XElement(Soap+"Body",new XElement(ns+(action+"Response"),new XAttribute(XNamespace.Xmlns+"u",ns),values.Select(v=>new XElement(v.Name,v.Value))))).ToString(SaveOptions.DisableFormatting);
    }
    internal static string Fault(int code,string description)
    {
        XNamespace ns="urn:schemas-upnp-org:control-1-0";
        return new XElement(Soap+"Envelope",new XAttribute(XNamespace.Xmlns+"s",Soap),new XElement(Soap+"Body",new XElement(Soap+"Fault",new XElement("faultcode","s:Client"),new XElement("faultstring","UPnPError"),
            new XElement("detail",new XElement(ns+"UPnPError",new XElement(ns+"errorCode",code),new XElement(ns+"errorDescription",description)))))).ToString(SaveOptions.DisableFormatting);
    }
    internal static string Control(string service,string soapAction,string xml,DlnaCatalog catalog,string baseUrl,uint updateId=1)
    {
        try
        {
            var document=ParseXml(xml);var body=document.Root?.Name==Soap+"Envelope"?document.Root.Element(Soap+"Body"):null;
            var call=body?.Elements().SingleOrDefault();if(call is null||call.Name.NamespaceName!=service||soapAction.Trim('"')!=service+"#"+call.Name.LocalName)throw new DlnaFault(401,"Invalid action");
            string action=call.Name.LocalName;var arguments=call.Elements().ToArray();
            if(arguments.Any(a=>a.HasElements)||arguments.Select(a=>a.Name.LocalName).Distinct().Count()!=arguments.Length)throw new DlnaFault(402,"Invalid arguments");
            string Arg(string name)=>arguments.SingleOrDefault(a=>a.Name.LocalName==name)?.Value??throw new DlnaFault(402,"Missing argument");
            void Args(params string[] names){if(arguments.Length!=names.Length||arguments.Any(a=>!names.Contains(a.Name.LocalName)))throw new DlnaFault(402,"Invalid arguments");}
            string Reply(params (string,object)[] values)=>SoapReply(service,action,values);
            if(service==Cd)
            {
                switch(action)
                {
                    case "GetSearchCapabilities":Args();return Reply(("SearchCaps",""));
                    case "GetSortCapabilities":Args();return Reply(("SortCaps","dc:title"));
                    case "GetSystemUpdateID":Args();return Reply(("Id",updateId));
                    case "Browse":
                        Args("ObjectID","BrowseFlag","Filter","StartingIndex","RequestedCount","SortCriteria");
                        string flag=Arg("BrowseFlag");if(flag is not ("BrowseMetadata" or "BrowseDirectChildren")||!uint.TryParse(Arg("StartingIndex"),NumberStyles.None,CultureInfo.InvariantCulture,out uint start)||!uint.TryParse(Arg("RequestedCount"),NumberStyles.None,CultureInfo.InvariantCulture,out uint count))throw new DlnaFault(402,"Invalid Browse arguments");
                        var result=catalog.Browse(Arg("ObjectID"),flag=="BrowseMetadata",start,count,Arg("SortCriteria"),Arg("Filter"),baseUrl);
                        return Reply(("Result",result.Xml),("NumberReturned",result.Returned),("TotalMatches",result.Total),("UpdateID",updateId));
                }
            }
            if(service==Cm)
            {
                switch(action)
                {
                    case "GetProtocolInfo":Args();return Reply(("Source",catalog.SourceProtocolInfo),("Sink",""));
                    case "GetCurrentConnectionIDs":Args();return Reply(("ConnectionIDs","0"));
                    case "GetCurrentConnectionInfo":Args("ConnectionID");if(Arg("ConnectionID")!="0")throw new DlnaFault(706,"Invalid connection reference");
                        return Reply(("RcsID",-1),("AVTransportID",-1),("ProtocolInfo",""),("PeerConnectionManager",""),("PeerConnectionID",-1),("Direction","Output"),("Status","OK"));
                }
            }
            throw new DlnaFault(401,"Invalid action");
        }
        catch(DlnaFault){throw;}catch(Exception ex)when(ex is XmlException or InvalidOperationException){throw new DlnaFault(402,"Invalid XML arguments");}
    }
    internal static string Scpd(bool contentDirectory)
    {
        XNamespace ns="urn:schemas-upnp-org:service-1-0";
        var actions=new List<XElement>();var states=new Dictionary<string,(string Type,bool Event,string[]? Allowed)>();
        void State(string name,string type,bool events=false,string[]? allowed=null)=>states.Add(name,(type,events,allowed));
        void Action(string name,params (string Name,string Direction,string State)[] args)=>actions.Add(new XElement(ns+"action",new XElement(ns+"name",name),args.Length==0?null:new XElement(ns+"argumentList",args.Select(a=>new XElement(ns+"argument",new XElement(ns+"name",a.Name),new XElement(ns+"direction",a.Direction),new XElement(ns+"relatedStateVariable",a.State))))));
        if(contentDirectory)
        {
            State("SearchCapabilities","string");State("SortCapabilities","string");State("SystemUpdateID","ui4",true);
            foreach(var name in new[]{"ObjectID","Result","Filter","SortCriteria"})State("A_ARG_TYPE_"+name,"string");
            State("A_ARG_TYPE_BrowseFlag","string",allowed:["BrowseMetadata","BrowseDirectChildren"]);
            foreach(var name in new[]{"Index","Count","UpdateID"})State("A_ARG_TYPE_"+name,"ui4");
            Action("GetSearchCapabilities",("SearchCaps","out","SearchCapabilities"));Action("GetSortCapabilities",("SortCaps","out","SortCapabilities"));Action("GetSystemUpdateID",("Id","out","SystemUpdateID"));
            Action("Browse",("ObjectID","in","A_ARG_TYPE_ObjectID"),("BrowseFlag","in","A_ARG_TYPE_BrowseFlag"),("Filter","in","A_ARG_TYPE_Filter"),("StartingIndex","in","A_ARG_TYPE_Index"),("RequestedCount","in","A_ARG_TYPE_Count"),("SortCriteria","in","A_ARG_TYPE_SortCriteria"),("Result","out","A_ARG_TYPE_Result"),("NumberReturned","out","A_ARG_TYPE_Count"),("TotalMatches","out","A_ARG_TYPE_Count"),("UpdateID","out","A_ARG_TYPE_UpdateID"));
        }
        else
        {
            State("SourceProtocolInfo","string",true);State("SinkProtocolInfo","string",true);State("CurrentConnectionIDs","string",true);
            foreach(var name in new[]{"ConnectionID","AVTransportID","RcsID"})State("A_ARG_TYPE_"+name,"i4");
            foreach(var name in new[]{"ProtocolInfo","ConnectionManager"})State("A_ARG_TYPE_"+name,"string");
            State("A_ARG_TYPE_Direction","string",allowed:["Input","Output"]);State("A_ARG_TYPE_ConnectionStatus","string",allowed:["OK","ContentFormatMismatch","InsufficientBandwidth","UnreliableChannel","Unknown"]);
            Action("GetProtocolInfo",("Source","out","SourceProtocolInfo"),("Sink","out","SinkProtocolInfo"));Action("GetCurrentConnectionIDs",("ConnectionIDs","out","CurrentConnectionIDs"));
            Action("GetCurrentConnectionInfo",("ConnectionID","in","A_ARG_TYPE_ConnectionID"),("RcsID","out","A_ARG_TYPE_RcsID"),("AVTransportID","out","A_ARG_TYPE_AVTransportID"),("ProtocolInfo","out","A_ARG_TYPE_ProtocolInfo"),("PeerConnectionManager","out","A_ARG_TYPE_ConnectionManager"),("PeerConnectionID","out","A_ARG_TYPE_ConnectionID"),("Direction","out","A_ARG_TYPE_Direction"),("Status","out","A_ARG_TYPE_ConnectionStatus"));
        }
        return new XElement(ns+"scpd",new XElement(ns+"specVersion",new XElement(ns+"major",1),new XElement(ns+"minor",0)),new XElement(ns+"actionList",actions),new XElement(ns+"serviceStateTable",states.Select(s=>new XElement(ns+"stateVariable",new XAttribute("sendEvents",s.Value.Event?"yes":"no"),new XElement(ns+"name",s.Key),new XElement(ns+"dataType",s.Value.Type),s.Value.Allowed is null?null:new XElement(ns+"allowedValueList",s.Value.Allowed.Select(v=>new XElement(ns+"allowedValue",v))))))).ToString(SaveOptions.DisableFormatting);
    }
    internal static (long Start,long End)? Range(string? value,long length)
    {
        if(value is null)return null;
        if(!value.StartsWith("bytes=",StringComparison.Ordinal)||value.Contains(','))throw new DlnaFault(416,"Invalid range");
        var parts=value[6..].Split('-');if(parts.Length!=2)throw new DlnaFault(416,"Invalid range");
        bool Number(string text,out long n)=>long.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out n);
        long start,end;
        if(parts[0].Length==0){if(!Number(parts[1],out long suffix)||suffix<=0)throw new DlnaFault(416,"Invalid range");start=Math.Max(0,length-suffix);end=length-1;}
        else{if(!Number(parts[0],out start)||start>=length)throw new DlnaFault(416,"Invalid range");if(parts[1].Length==0)end=length-1;else if(!Number(parts[1],out end)||end<start)throw new DlnaFault(416,"Invalid range");end=Math.Min(end,length-1);}
        return(start,end);
    }
}
