using dotnetCampus.Ipc.CompilerServices.Attributes;
using System.Threading.Tasks;

[IpcPublic]
public interface IMyService {
    Task<MyResult> DoWork(MyRequest req);
}

public class MyResult { public string Msg {get;set;} }
public class MyRequest { public string Data {get;set;} }
