using System.Collections.Generic;

namespace drvVesy31
{
    public class ControllerMessage
    {
        public Dictionary<string, string> setOfValues { get; set; } = new Dictionary<string, string>();

        public bool wasError { get; set; } = false;
    }
}
