using System;
using System.IO;
using System.Runtime.InteropServices;

namespace clip.Native;

/// <summary>
/// Initiates a native OLE drag-and-drop operation.
/// Runs as a blocking nested modal loop on the WinUI 3 App dispatcher.
/// </summary>
public static class DragDropService
{
    private const int DROPEFFECT_COPY = 1;
    private const int DROPEFFECT_MOVE = 2;

    public static void StartDrag(string[]? filePaths, string? textContext)
    {
        bool hasFiles = filePaths != null && filePaths.Length > 0;
        bool hasText = !string.IsNullOrEmpty(textContext);

        if (!hasFiles && !hasText) return;

        try
        {
            Win32Helper.OleInitialize(IntPtr.Zero);

            var dataObject = new MultiFormatDataObject(filePaths, textContext);
            var dropSource = new SimpleDropSource();
            
            DoDragDrop(dataObject, dropSource, DROPEFFECT_COPY | DROPEFFECT_MOVE, out int effect);
            System.Diagnostics.Debug.WriteLine($"[DragDrop] Ended, effect={effect}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DragDrop] Error: {ex.Message}");
        }
    }

    [ComImport, Guid("0000010E-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IOleDataObject
    {
        [PreserveSig] int GetData(ref System.Runtime.InteropServices.ComTypes.FORMATETC format, out System.Runtime.InteropServices.ComTypes.STGMEDIUM medium);
        [PreserveSig] int GetDataHere(ref System.Runtime.InteropServices.ComTypes.FORMATETC format, ref System.Runtime.InteropServices.ComTypes.STGMEDIUM medium);
        [PreserveSig] int QueryGetData(ref System.Runtime.InteropServices.ComTypes.FORMATETC format);
        [PreserveSig] int GetCanonicalFormatEtc(ref System.Runtime.InteropServices.ComTypes.FORMATETC formatIn, out System.Runtime.InteropServices.ComTypes.FORMATETC formatOut);
        [PreserveSig] int SetData(ref System.Runtime.InteropServices.ComTypes.FORMATETC formatIn, ref System.Runtime.InteropServices.ComTypes.STGMEDIUM medium, bool release);
        [PreserveSig] int EnumFormatEtc(int dwDirection, out System.Runtime.InteropServices.ComTypes.IEnumFORMATETC? enumerator);
        [PreserveSig] int DAdvise(ref System.Runtime.InteropServices.ComTypes.FORMATETC pFormatetc, int advf, IntPtr pAdvSink, out int pdwConnection);
        [PreserveSig] int DUnadvise(int dwConnection);
        [PreserveSig] int EnumDAdvise(out IntPtr ppenumAdvise);
    }

    private sealed class MultiFormatDataObject : IOleDataObject
    {
        private readonly string[]? _paths;
        private readonly string? _text;
        private readonly bool _hasFiles;
        private readonly bool _hasText;

        public MultiFormatDataObject(string[]? paths, string? text)
        {
            _paths = paths;
            _text = text;
            _hasFiles = paths != null && paths.Length > 0;
            _hasText = !string.IsNullOrEmpty(text);
        }

        public int GetData(ref System.Runtime.InteropServices.ComTypes.FORMATETC format,
                           out System.Runtime.InteropServices.ComTypes.STGMEDIUM medium)
        {
            medium = new System.Runtime.InteropServices.ComTypes.STGMEDIUM();

            if (_hasFiles && format.cfFormat == (short)Win32Helper.CF_HDROP)
            {
                using var ms = new MemoryStream();
                using var bw = new BinaryWriter(ms, System.Text.Encoding.Unicode, leaveOpen: true);
                bw.Write(20u);  // pFiles
                bw.Write(0);    // pt.X
                bw.Write(0);    // pt.Y
                bw.Write(0);    // fNC
                bw.Write(1);    // fWide
                foreach (var p in _paths!)
                {
                    bw.Write(System.Text.Encoding.Unicode.GetBytes(p));
                    bw.Write((short)0);
                }
                bw.Write((short)0);
                bw.Flush();

                byte[] data = ms.ToArray();
                IntPtr hGlobal = Marshal.AllocHGlobal(data.Length);
                Marshal.Copy(data, 0, hGlobal, data.Length);

                medium.tymed = System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL;
                medium.unionmember = hGlobal;
                medium.pUnkForRelease = null;
                return 0; // S_OK
            }

            if (_hasText && format.cfFormat == (short)Win32Helper.CF_UNICODETEXT)
            {
                byte[] data = System.Text.Encoding.Unicode.GetBytes(_text + "\0");
                IntPtr hGlobal = Marshal.AllocHGlobal(data.Length);
                Marshal.Copy(data, 0, hGlobal, data.Length);

                medium.tymed = System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL;
                medium.unionmember = hGlobal;
                medium.pUnkForRelease = null;
                return 0; // S_OK
            }

            return unchecked((int)0x80040064); // DV_E_FORMATETC
        }

        public int GetDataHere(ref System.Runtime.InteropServices.ComTypes.FORMATETC f,
                               ref System.Runtime.InteropServices.ComTypes.STGMEDIUM m)
            => unchecked((int)0x80040064);

        public int QueryGetData(ref System.Runtime.InteropServices.ComTypes.FORMATETC f)
        {
            if (_hasFiles && f.cfFormat == (short)Win32Helper.CF_HDROP) return 0;
            if (_hasText && f.cfFormat == (short)Win32Helper.CF_UNICODETEXT) return 0;
            return unchecked((int)0x80040064);
        }

        public int GetCanonicalFormatEtc(ref System.Runtime.InteropServices.ComTypes.FORMATETC fIn,
                                         out System.Runtime.InteropServices.ComTypes.FORMATETC fOut)
        {
            fOut = fIn;
            return unchecked((int)0x80040064);
        }

        public int SetData(ref System.Runtime.InteropServices.ComTypes.FORMATETC fmt,
                           ref System.Runtime.InteropServices.ComTypes.STGMEDIUM med, bool release)
            => unchecked((int)0x80040064);

        public int EnumFormatEtc(int dir, out System.Runtime.InteropServices.ComTypes.IEnumFORMATETC? enumerator)
        {
            var formats = new System.Collections.Generic.List<System.Runtime.InteropServices.ComTypes.FORMATETC>();
            if (_hasFiles)
            {
                formats.Add(new System.Runtime.InteropServices.ComTypes.FORMATETC
                {
                    cfFormat = (short)Win32Helper.CF_HDROP,
                    dwAspect = System.Runtime.InteropServices.ComTypes.DVASPECT.DVASPECT_CONTENT,
                    lindex   = -1,
                    tymed    = System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL
                });
            }
            if (_hasText)
            {
                formats.Add(new System.Runtime.InteropServices.ComTypes.FORMATETC
                {
                    cfFormat = (short)Win32Helper.CF_UNICODETEXT,
                    dwAspect = System.Runtime.InteropServices.ComTypes.DVASPECT.DVASPECT_CONTENT,
                    lindex   = -1,
                    tymed    = System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL
                });
            }

            int hr = Win32Helper.SHCreateStdEnumFmtEtc((uint)formats.Count, formats.ToArray(), out var enumFmt);
            enumerator = enumFmt;
            return hr;
        }

        public int DAdvise(ref System.Runtime.InteropServices.ComTypes.FORMATETC f, int adv, IntPtr sink, out int conn)
        { conn = 0; return unchecked((int)0x80004001); } 

        public int DUnadvise(int conn) => unchecked((int)0x80004001);
        public int EnumDAdvise(out IntPtr e) { e = IntPtr.Zero; return unchecked((int)0x80004001); }
    }

    [ComImport, Guid("00000121-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDropSource
    {
        [PreserveSig] int QueryContinueDrag(int fEscapePressed, uint grfKeyState);
        [PreserveSig] int GiveFeedback(int dwEffect);
    }

    private sealed class SimpleDropSource : IDropSource
    {
        public int QueryContinueDrag(int fEscapePressed, uint grfKeyState)
        {
            if (fEscapePressed != 0) return unchecked((int)0x80040101); // DRAGDROP_S_CANCEL
            if ((grfKeyState & 0x01) == 0) return unchecked((int)0x00040100); // DRAGDROP_S_DROP
            return 0; // S_OK
        }

        public int GiveFeedback(int dwEffect) => unchecked((int)0x80040102); // DRAGDROP_S_USEDEFAULTCURSORS
    }

    [DllImport("ole32.dll")]
    private static extern int DoDragDrop(
        [MarshalAs(UnmanagedType.Interface)] IOleDataObject pDataObj,
        [MarshalAs(UnmanagedType.Interface)] IDropSource pDropSource,
        int dwOKEffects, out int pdwEffect);
}
