using System.Runtime.InteropServices;

namespace LuauSharp;

public unsafe class LuaFunctionWrapper : IDisposable
{
    private Luau.lua_State* luaState;
    private int index;
    private List<GCHandle> handles = new();

    internal LuaFunctionWrapper(Luau.lua_State* luaState, int index)
    {
        this.luaState = luaState;
        this.index = index;
    }

    public void Call(params object[] args)
    {
        Luau.lua_pushvalue(luaState, index);
        foreach (var arg in args)
            UserData.PushValueToLua(luaState, arg, ref handles);
        Luau.lua_call(luaState, args.Length, 0);
    }

    public T? Call<T>(params object[] args)
    {
        Luau.lua_pushvalue(luaState, index);
        foreach (var arg in args)
            UserData.PushValueToLua(luaState, arg, ref handles);
        Luau.lua_call(luaState, args.Length, 1);
        object? o = UserData.GetLuaValue(luaState, -1);
        if (o != null && typeof(T).IsPrimitive)
            o = Convert.ChangeType(o, typeof(T));
        return (T?) o;
    }

    public void Dispose() => handles.ForEach(x => x.Free());
}