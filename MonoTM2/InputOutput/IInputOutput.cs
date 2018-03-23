using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MonoTM2.InputOutput
{
    public interface IInputOutput
    {
       void OutputMessage(string msg, MessageType msgType);
    }
}
