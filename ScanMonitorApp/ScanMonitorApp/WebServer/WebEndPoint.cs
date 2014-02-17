using System;
using System.Collections.Generic;
using System.Net.Sockets;

namespace ScanMonitorApp
{
    public delegate string EndPointAction(List<string> items);

    public class WebEndPoint
    {
        public EndPointAction Action
        {
            private get; set;
        }

        public string Name { get; set; }
        public string Description { get; set; }
        public List<string> Arguments { get; set; }
        public String Execute()
        {
            if (Action != null)
            {
                return Action(Arguments);
            }
            return "Unknown action";
        }
    }

}
