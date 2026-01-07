using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using Infrastructure;

namespace VasaLiveFeeder
{
    public class LiveFeederConfig : ConfigFile
    {
        [DefaultValue("Phi-3.5-mini-instruct-cuda-gpu")]
        public string ModelName { get; set; }

        [DefaultValue(@"A successful run should contain at least 10 log entries.
Outcome 0 is success, 1 is Remark and 2 is Fail.
A successful run should not have any failed outcomes.
Please analyze the entries with failed outcomes and try to provide a root cause.
")]
        public string Context { get; set; }
    }
}
