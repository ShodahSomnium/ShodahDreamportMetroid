using System.Collections.Generic;

namespace SomniumSpace.Worlds.SomniumMetroid.Data
{
    /// <summary>
    /// SCLY (decomp CStateManager::LoadScriptObjects / LoadScriptObject): "SCLY", u32 version, u32 layer count, u32 layer sizes,
    /// then per layer: u8 version, u32 object count, objects (u8 type, u32 size, u32 editor id, u32 connection count + (u32 state,
    /// u32 message, u32 target id), u32 property count, properties). Properties start with the instance name (zero-terminated).
    /// The typed decoders (MpScriptObjects.cs) follow ScriptLoader.cpp field by field.
    /// </summary>
    public sealed class MpScriptObject
    {
        public struct Link
        {
            public int State, Message;
            public uint Target;
        }

        public int Type;
        public uint Id;
        public int Layer;
        public int PropCount;
        public readonly List<Link> Links = new List<Link>();
        public byte[] Data;
        public int PropAt, PropEnd;
        public string Name;

        /// <summary>A reader at the first property (the name already read off: positioned right after it).</summary>
        public MpReader Props()
        {
            var r = new MpReader(Data, PropAt, PropEnd - PropAt);
            r.CString();
            return r;
        }

        public int AreaIndexOfId { get { return (int)((Id >> 16) & 0x3FF); } }
    }

    public static class MpScriptLayers
    {
        public static List<MpScriptObject> Read(MpArea area)
        {
            var list = new List<MpScriptObject>();
            if (area.ScriptSection < 0 || area.ScriptSection >= area.SectionCount) return list;
            var r = area.Section(area.ScriptSection);
            if (r.FourCC() != "SCLY") return list;
            r.U32();
            int layers = r.S32();
            var sizes = new int[layers];
            for (int i = 0; i < layers; i++) sizes[i] = r.S32();
            int at = r.Pos;
            for (int l = 0; l < layers; l++)
            {
                var lr = new MpReader(area.Data, at, sizes[l]);
                at += sizes[l];
                lr.U8();
                int count = lr.S32();
                for (int i = 0; i < count; i++)
                {
                    var o = new MpScriptObject { Layer = l, Data = area.Data };
                    o.Type = lr.U8();
                    int size = lr.S32();
                    int end = lr.Pos + size;
                    o.Id = lr.U32();
                    int links = lr.S32();
                    for (int k = 0; k < links; k++) o.Links.Add(new MpScriptObject.Link { State = lr.S32(), Message = lr.S32(), Target = lr.U32() });
                    o.PropCount = lr.S32();
                    o.PropAt = lr.Pos;
                    o.PropEnd = end;
                    var nr = new MpReader(area.Data, o.PropAt, end - o.PropAt);
                    o.Name = nr.CString();
                    lr.Pos = end;
                    list.Add(o);
                }
            }

            return list;
        }
    }
}
