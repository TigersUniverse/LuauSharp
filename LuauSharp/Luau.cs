using System.Runtime.InteropServices;

namespace LuauSharp;

internal unsafe class Luau
{
#if MSVC
    private const string VM = "Luau.VM.dll";
    private const string COMPILER = "Luau.Compiler.dll";
#elif MAC
    private const string VM = "libLuau.VM.dylib";
    private const string COMPILER = "libLuau.Compiler.dylib";
#else
    private const string VM = "libLuau.VM.so";
    private const string COMPILER = "libLuau.Compiler.so";
#endif
    
    public const int LUA_MULTRET = -1;
    public const int LUAI_MAXCSTACK = 8000;
    public const int LUA_REGISTRYINDEX = -LUAI_MAXCSTACK - 2000;
    public const int LUA_ENVIRONINDEX = -LUAI_MAXCSTACK - 2001;
    public const int LUA_GLOBALSINDEX = -LUAI_MAXCSTACK - 2002;

    #region Enums

    public enum lua_Type
    {
        LUA_TNIL = 0,
        LUA_TBOOLEAN = 1,


        LUA_TLIGHTUSERDATA,
        LUA_TNUMBER,
        LUA_TVECTOR,

        LUA_TSTRING,


        LUA_TTABLE,
        LUA_TFUNCTION,
        LUA_TUSERDATA,
        LUA_TTHREAD,
        LUA_TBUFFER,
        
        LUA_TPROTO,
        LUA_TUPVAL,
        LUA_TDEADKEY,
        
        LUA_T_COUNT = LUA_TPROTO
    };

    public enum lua_Status : uint
    {
        LUA_OK,
        LUA_YIELD,
        LUA_ERRRUN,
        LUA_ERRSYNTAX,
        LUA_ERRMEM,
        LUA_ERRERR,
        LUA_BREAK,
    }

    #endregion

    #region Structs

    [StructLayout(LayoutKind.Sequential)]
    public struct lua_State
    {
        public byte tt;
        public byte marked;
        public byte memcat;
        public byte status;
        public byte activememcat;
        [MarshalAs(UnmanagedType.I1)]
        public bool isactive;
        [MarshalAs(UnmanagedType.I1)]
        public bool singlestep;

        public IntPtr top;
        public IntPtr @base;
        public IntPtr global;
        public IntPtr ci;
        public IntPtr stack_last;
        public IntPtr stack;
        public IntPtr end_ci;
        public IntPtr base_ci;

        public int stacksize;
        public int size_ci;

        public ushort nCcalls;
        public ushort baseCcalls;

        public int cachedslot;

        public IntPtr gt;
        public IntPtr openupval;
        public IntPtr gclist;
        public IntPtr namecall;
        public IntPtr userdata;
    }
    
    [StructLayout(LayoutKind.Sequential)]
    public struct lua_CompileOptions
    {
        public int optimizationLevel;
        public int debugLevel;
        public int typeInfoLevel;
        public int coverageLevel;

        public IntPtr vectorLib;
        public IntPtr vectorCtor;
        public IntPtr vectorType;

        public IntPtr mutableGlobals;
        public IntPtr userdataTypes;

        public lua_CompileOptions()
        {
            int optimizationLevel = 1;
            int debugLevel = 1;
            int typeInfoLevel = 0;
            int coverageLevel = 0;
        }
    }

    #endregion
    
    #region Native Delegates
    
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int LuaCFunction(lua_State* L);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int LuaContinuation(lua_State* L, int status);
    
    #endregion

    #region Helper Functions

    public static int lua_upvalueindex(int i) => LUA_GLOBALSINDEX - i;
    public static bool lua_isboolean(lua_State* L, int idx) => lua_type(L, idx) == (int) lua_Type.LUA_TBOOLEAN;
    public static bool lua_isnil(lua_State* L, int idx) => lua_type(L, idx) == (int) lua_Type.LUA_TNIL;
    public static bool lua_isfunction(lua_State* L, int idx) => lua_type(L, idx) == (int) lua_Type.LUA_TFUNCTION;

    #endregion
    
    #region StateControl
    
    [DllImport(VM, CallingConvention = CallingConvention.Cdecl)]
    public static extern lua_State* luaL_newstate();
    
    [DllImport(VM, CallingConvention = CallingConvention.Cdecl)]
    public static extern void lua_setsafeenv(lua_State* L, int index, int value);

    [DllImport(VM, CallingConvention = CallingConvention.Cdecl)]
    public static extern void luaL_openlibs(lua_State* L);
    
    [DllImport(VM, CallingConvention = CallingConvention.Cdecl)]
    public static extern void lua_close(lua_State* L);
    
    #endregion

    #region Loading

    [DllImport(COMPILER, CharSet = CharSet.Ansi, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr luau_compile(string source, IntPtr size, IntPtr options, out IntPtr outsize);

    [DllImport(VM, CallingConvention = CallingConvention.Cdecl)]
    public static extern int luau_load(lua_State* L, string name, byte[] bytecode, long size, int flags);

    #endregion

    #region Type Conversion
    
    [DllImport(VM, CallingConvention = CallingConvention.Cdecl)]
    public static extern int lua_isnumber(lua_State* L, int idx);
    
    [DllImport(VM, CallingConvention = CallingConvention.Cdecl)]
    public static extern int lua_isstring(lua_State* L, int idx);
    
    [DllImport(VM, CallingConvention = CallingConvention.Cdecl)]
    public static extern int lua_isuserdata(lua_State* L, int idx);

    [DllImport(VM, CallingConvention = CallingConvention.Cdecl)]
    public static extern int lua_type(lua_State* L, int idx);

    [DllImport(VM, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr lua_tolstring(lua_State* L, int idx, out IntPtr len);
    
    [DllImport(VM, CallingConvention = CallingConvention.Cdecl)]
    public static extern int lua_tointegerx(lua_State* L, int idx, out int isnum);
    
    [DllImport(VM, CallingConvention = CallingConvention.Cdecl)]
    public static extern double lua_tonumberx(lua_State* L, int idx, out int isnum);

    [DllImport(VM, CallingConvention = CallingConvention.Cdecl)]
    public static extern int lua_toboolean(lua_State* L, int idx);
    
    [DllImport(VM, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr lua_tolightuserdatatagged(lua_State* luaState, int idx, int tag);

    [DllImport(VM, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr lua_tocfunction(lua_State* luaState, int idx);

    #endregion

    #region Functions

    [DllImport(VM, CallingConvention = CallingConvention.Cdecl)]
    public static extern int lua_pcall(lua_State* L, int nargs, int nresults, int msgh);
    
    [DllImport(VM, CallingConvention = CallingConvention.Cdecl)]
    public static extern void lua_setfield(lua_State* L, int idx, string k);

    [DllImport(VM, CallingConvention = CallingConvention.Cdecl)]
    public static extern int lua_setmetatable(lua_State* L, int objindex);

    [DllImport(VM, CallingConvention = CallingConvention.Cdecl)]
    public static extern int lua_gettop(lua_State* L);

    [DllImport(VM, CallingConvention = CallingConvention.Cdecl)]
    public static extern void lua_call(lua_State* L, int nargs, int nresults);

    #endregion

    #region Variable Pushing

    [DllImport(VM, CallingConvention = CallingConvention.Cdecl)]
    public static extern void lua_createtable(lua_State* L, int narr, int n);
    
    [DllImport(VM, CallingConvention = CallingConvention.Cdecl)]
    public static extern void lua_pushlightuserdatatagged(lua_State* luaState, IntPtr p, int tag);
    
    [DllImport(VM, CallingConvention = CallingConvention.Cdecl)]
    public static extern void lua_pushcclosurek(lua_State* L, LuaCFunction fn, string debugname, int nup, LuaContinuation cont);

    [DllImport(VM, CallingConvention = CallingConvention.Cdecl)]
    public static extern void lua_pushnil(lua_State* L);
    
    [DllImport(VM, CallingConvention = CallingConvention.Cdecl)]
    public static extern void lua_pushboolean(lua_State* L, int b);
    
    [DllImport(VM, CallingConvention = CallingConvention.Cdecl)]
    public static extern void lua_pushstring(lua_State* L, string s);
    
    [DllImport(VM, CallingConvention = CallingConvention.Cdecl)]
    public static extern void lua_pushinteger(lua_State* L, int n);
    
    [DllImport(VM, CallingConvention = CallingConvention.Cdecl)]
    public static extern void lua_pushnumber(lua_State* L, double n);

    [DllImport(VM, CallingConvention = CallingConvention.Cdecl)]
    public static extern void lua_pushvalue(lua_State* L, int idx);

    #endregion
}