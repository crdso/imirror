using System.Buffers.Binary;
using iMirror.Input;

internal static class RawMouseProbe
{
    private static void Require(bool value) { if (!value) { throw new Exception("Raw motion invariant failed"); } }
    public static void Check()
    {
        foreach(int header in new[]{16,24})
        {
            var packet=new byte[header+24]; BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(4),(uint)packet.Length);
            BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(header+12),-29);
            BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(header+16),71);
            Require(RawMouseMotion.Decode(packet,header)==(-29,71));
            var motion=new RelativeMotion(); Require(motion.Add(-29,71,1)==(-29,71));
            packet[header]=1; Require(RawMouseMotion.Decode(packet,header) is null); packet[header]=8; Require(RawMouseMotion.Decode(packet,header)==(-29,71));
            Require(RawMouseMotion.Decode(packet.AsSpan(0,packet.Length-1),header) is null);
            packet[0]=1; Require(RawMouseMotion.Decode(packet,header) is null); packet[0]=0;
            packet[4]=1; Require(RawMouseMotion.Decode(packet,header) is null);
        }
        Require(RawMouseMotion.Decode([],24) is null && RawMouseMotion.Decode(new byte[48],20) is null);
    }
}
