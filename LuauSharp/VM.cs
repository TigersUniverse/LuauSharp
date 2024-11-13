using System.Runtime.InteropServices;

namespace LuauSharp;

public class VM : IDisposable
{
    private unsafe Luau.lua_State* state;
    public UserData UserData { get; }
    private IntPtr optionsPtr;
    private bool loaded;

    private Action<object> print;
    private Action<object> warn;
    private Action<object> error;
    
    public VM(Action<object> print, Action<object> warn, Action<object> error)
    {
        this.print = print;
        this.warn = warn;
        this.error = error;
        Luau.lua_CompileOptions options = new Luau.lua_CompileOptions();
        optionsPtr = Marshal.AllocHGlobal(Marshal.SizeOf(options));
        Marshal.StructureToPtr(options, optionsPtr, false);
        unsafe
        {
            state = Luau.luaL_newstate();
            UserData = new UserData(state);
            Luau.lua_setsafeenv(state, Luau.LUA_ENVIRONINDEX, 1);
            Luau.luaL_openlibs(state);
        }
        UserData.PushFunction("print", print);
        UserData.PushFunction("warn", warn);
        UserData.PushFunction("error", error);
    }

    public byte[] Compile(string sourceCode)
    {
        IntPtr size = (IntPtr) sourceCode.Length;
        IntPtr outSize;
        IntPtr bytecodePtr = Luau.luau_compile(sourceCode, size, optionsPtr, out outSize);
        if (bytecodePtr == IntPtr.Zero)
        {
            Marshal.FreeHGlobal(optionsPtr);
            throw new Exception("Error compiling Lua source code");
        }
        byte[] bytecode = new byte[outSize.ToInt32()];
        Marshal.Copy(bytecodePtr, bytecode, 0, bytecode.Length);
        Marshal.FreeHGlobal(bytecodePtr);
        return bytecode;
    }

    private unsafe int Load(string name, byte[] compiled)
    {
        if (loaded)
            throw new Exception("Cannot load when already loaded! Please dispose this VM and create a new one");
        int loadResult = Luau.luau_load(state, name, compiled, compiled.Length, 0);
        if (loadResult != 0)
        {
            error.Invoke("Error loading bytecode");
            Luau.lua_close(state);
            Marshal.FreeHGlobal(optionsPtr);
        }
        loaded = loadResult == 0;
        return loadResult;
    }

    public bool DoCompiled(string name, byte[] data) => Load(name, data) == 0;

    public bool DoText(string name, string data)
    {
        // Compile the code
        byte[] bytecode = Compile(data);
        // Load the compiled bytecode
        return Load(name, bytecode) == 0;
    }

    public bool DoFile(string pathToFile)
    {
        if (!File.Exists(pathToFile))
            throw new FileNotFoundException();
        return DoText(Path.GetFileName(pathToFile), File.ReadAllText(pathToFile));
    }

    public void Execute()
    {
        if (!loaded)
            throw new Exception("Cannot execute when not loaded!");
        unsafe
        {
            // Execute the loaded bytecode
            int runResult = Luau.lua_pcall(state, 0, Luau.LUA_MULTRET, 0);
            if (runResult == 0) return;
            error.Invoke(UserData.ReadString(-1) ?? "unknown error");
        }
    }

    public void Dispose()
    {
        unsafe
        {
            Luau.lua_close(state);
            Marshal.FreeHGlobal(optionsPtr);
        }
    }
}