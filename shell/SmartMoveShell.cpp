// Smart Move - handler di trascinamento (tasto destro) per Esplora file.
// Aggiunge "Smart Move qui" al menu che compare quando si trascina con il tasto destro in una cartella.
// La DLL gira dentro explorer.exe: fa il minimo (scrive l'elenco dei file e lancia SmartMove.exe).
#define WIN32_LEAN_AND_MEAN
#define UNICODE
#define _UNICODE
#include <windows.h>
#include <shlobj.h>
#include <shellapi.h>
#include <shlwapi.h>
#include <string>
#include <vector>
#include <new>

// {5B7F2C1A-8E34-4D6B-9A52-3C1D0E7F4A19}
static const CLSID CLSID_SmartMoveMenu =
    {0x5b7f2c1a, 0x8e34, 0x4d6b, {0x9a, 0x52, 0x3c, 0x1d, 0x0e, 0x7f, 0x4a, 0x19}};
static const wchar_t* kClsidStr = L"{5B7F2C1A-8E34-4D6B-9A52-3C1D0E7F4A19}";
static const wchar_t* kMenuText = L"Smart Move qui";

static long g_refs = 0;
static HINSTANCE g_inst = nullptr;

static std::wstring DllDir() {
    wchar_t p[MAX_PATH * 2];
    DWORD n = GetModuleFileNameW(g_inst, p, MAX_PATH * 2);
    std::wstring s(p, n);
    size_t i = s.find_last_of(L'\\');
    return i == std::wstring::npos ? L"" : s.substr(0, i);
}

class SmartMoveMenu : public IShellExtInit, public IContextMenu {
    long ref_ = 1;
    std::wstring dest_;
    std::vector<std::wstring> files_;
public:
    SmartMoveMenu() { InterlockedIncrement(&g_refs); }
    ~SmartMoveMenu() { InterlockedDecrement(&g_refs); }

    // IUnknown
    STDMETHODIMP QueryInterface(REFIID r, void** o) override {
        if (!o) return E_POINTER;
        if (r == IID_IUnknown || r == IID_IShellExtInit) *o = static_cast<IShellExtInit*>(this);
        else if (r == IID_IContextMenu) *o = static_cast<IContextMenu*>(this);
        else { *o = nullptr; return E_NOINTERFACE; }
        AddRef(); return S_OK;
    }
    STDMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&ref_); }
    STDMETHODIMP_(ULONG) Release() override {
        long r = InterlockedDecrement(&ref_); if (r == 0) delete this; return r;
    }

    // IShellExtInit: pidlFolder = cartella di destinazione, pdtobj = elementi trascinati
    STDMETHODIMP Initialize(PCIDLIST_ABSOLUTE pidlFolder, IDataObject* pdtobj, HKEY) override {
        try {
            if (!pidlFolder || !pdtobj) return E_INVALIDARG;
            wchar_t d[MAX_PATH * 2];
            if (!SHGetPathFromIDListW(pidlFolder, d)) return E_FAIL;   // niente cartelle virtuali
            dest_ = d;
            FORMATETC fe = {CF_HDROP, nullptr, DVASPECT_CONTENT, -1, TYMED_HGLOBAL};
            STGMEDIUM sm = {};
            if (FAILED(pdtobj->GetData(&fe, &sm))) return E_FAIL;
            HDROP h = (HDROP)GlobalLock(sm.hGlobal);
            if (h) {
                UINT n = DragQueryFileW(h, 0xFFFFFFFF, nullptr, 0);
                for (UINT i = 0; i < n; i++) {
                    UINT len = DragQueryFileW(h, i, nullptr, 0);
                    std::wstring f(len + 1, L'\0');
                    DragQueryFileW(h, i, &f[0], len + 1);
                    f.resize(len);
                    files_.push_back(f);
                }
                GlobalUnlock(sm.hGlobal);
            }
            ReleaseStgMedium(&sm);
            return files_.empty() ? E_FAIL : S_OK;
        } catch (...) { return E_FAIL; }
    }

    // IContextMenu
    STDMETHODIMP QueryContextMenu(HMENU menu, UINT pos, UINT idFirst, UINT, UINT flags) override {
        if (flags & CMF_DEFAULTONLY) return MAKE_HRESULT(SEVERITY_SUCCESS, FACILITY_NULL, 0);
        InsertMenuW(menu, pos, MF_BYPOSITION | MF_STRING, idFirst, kMenuText);
        InsertMenuW(menu, pos + 1, MF_BYPOSITION | MF_STRING, idFirst + 1, L"Smart Copy qui");
        return MAKE_HRESULT(SEVERITY_SUCCESS, FACILITY_NULL, 2);
    }

    STDMETHODIMP InvokeCommand(CMINVOKECOMMANDINFO* ci) override {
        try {
            if (HIWORD(ci->lpVerb) != 0) return E_INVALIDARG;
            int verb = LOWORD(ci->lpVerb);
            if (verb > 1) return E_INVALIDARG;

            wchar_t tmpDir[MAX_PATH], tmp[MAX_PATH];
            GetTempPathW(MAX_PATH, tmpDir);
            if (!GetTempFileNameW(tmpDir, L"smv", 0, tmp)) return E_FAIL;
            HANDLE f = CreateFileW(tmp, GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_TEMPORARY, nullptr);
            if (f == INVALID_HANDLE_VALUE) return E_FAIL;
            // elenco UTF-8, un percorso per riga (letto da File.ReadAllLines)
            for (auto& s : files_) {
                std::wstring line = s + L"\r\n";
                int n = WideCharToMultiByte(CP_UTF8, 0, line.c_str(), (int)line.size(), nullptr, 0, nullptr, nullptr);
                std::string u(n, '\0');
                WideCharToMultiByte(CP_UTF8, 0, line.c_str(), (int)line.size(), &u[0], n, nullptr, nullptr);
                DWORD w; WriteFile(f, u.data(), (DWORD)u.size(), &w, nullptr);
            }
            CloseHandle(f);

            std::wstring dest = dest_;
            while (dest.size() > 3 && (dest.back() == L'\\')) dest.pop_back();  // evita \" nella riga di comando
            std::wstring exe = DllDir() + L"\\SmartMove.exe";
            std::wstring cmd = L"\"" + exe + L"\" --job \"" + tmp + L"\" \"" + dest + L"\" " + (verb == 0 ? L"move" : L"copy");
            STARTUPINFOW si = {sizeof si}; PROCESS_INFORMATION pi = {};
            if (!CreateProcessW(exe.c_str(), &cmd[0], nullptr, nullptr, FALSE, 0, nullptr, DllDir().c_str(), &si, &pi)) {
                DeleteFileW(tmp);
                MessageBoxW(ci->hwnd, L"Impossibile avviare SmartMove.exe (deve stare accanto alla DLL).", L"Smart Move", MB_ICONERROR);
                return E_FAIL;
            }
            CloseHandle(pi.hThread); CloseHandle(pi.hProcess);
            return S_OK;
        } catch (...) { return E_FAIL; }
    }

    STDMETHODIMP GetCommandString(UINT_PTR, UINT type, UINT*, CHAR* name, UINT cch) override {
        if (type == GCS_HELPTEXTW) { lstrcpynW((LPWSTR)name, L"Sposta con la regola Smart Move", cch); return S_OK; }
        return E_NOTIMPL;
    }
};

class Factory : public IClassFactory {
    long ref_ = 1;
public:
    STDMETHODIMP QueryInterface(REFIID r, void** o) override {
        if (r == IID_IUnknown || r == IID_IClassFactory) { *o = static_cast<IClassFactory*>(this); AddRef(); return S_OK; }
        *o = nullptr; return E_NOINTERFACE;
    }
    STDMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&ref_); }
    STDMETHODIMP_(ULONG) Release() override { long r = InterlockedDecrement(&ref_); if (!r) delete this; return r; }
    STDMETHODIMP CreateInstance(IUnknown* outer, REFIID r, void** o) override {
        if (outer) return CLASS_E_NOAGGREGATION;
        auto* p = new (std::nothrow) SmartMoveMenu();
        if (!p) return E_OUTOFMEMORY;
        HRESULT hr = p->QueryInterface(r, o); p->Release(); return hr;
    }
    STDMETHODIMP LockServer(BOOL l) override { if (l) InterlockedIncrement(&g_refs); else InterlockedDecrement(&g_refs); return S_OK; }
};


// ================= Drop target (trascinamento col tasto SINISTRO) =================
// {5B7F2C1A-8E34-4D6B-9A52-3C1D0E7F4A1A}
static const CLSID CLSID_SmartMoveDrop =
    {0x5b7f2c1a, 0x8e34, 0x4d6b, {0x9a, 0x52, 0x3c, 0x1d, 0x0e, 0x7f, 0x4a, 0x1a}};

static bool GetDropFiles(IDataObject* d, std::vector<std::wstring>& out) {
    FORMATETC fe = {CF_HDROP, nullptr, DVASPECT_CONTENT, -1, TYMED_HGLOBAL};
    STGMEDIUM sm = {};
    if (FAILED(d->GetData(&fe, &sm))) return false;
    HDROP h = (HDROP)GlobalLock(sm.hGlobal);
    if (h) {
        UINT n = DragQueryFileW(h, 0xFFFFFFFF, nullptr, 0);
        for (UINT i = 0; i < n; i++) {
            UINT len = DragQueryFileW(h, i, nullptr, 0);
            std::wstring f(len + 1, L'\0'); DragQueryFileW(h, i, &f[0], len + 1); f.resize(len);
            out.push_back(f);
        }
        GlobalUnlock(sm.hGlobal);
    }
    ReleaseStgMedium(&sm);
    return !out.empty();
}

static bool LaunchJob(const std::vector<std::wstring>& files, const std::wstring& destIn, bool copy) {
    wchar_t tmpDir[MAX_PATH], tmp[MAX_PATH];
    GetTempPathW(MAX_PATH, tmpDir);
    if (!GetTempFileNameW(tmpDir, L"smv", 0, tmp)) return false;
    HANDLE f = CreateFileW(tmp, GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_TEMPORARY, nullptr);
    if (f == INVALID_HANDLE_VALUE) return false;
    for (auto& s : files) {
        std::wstring line = s + L"\r\n";
        int n = WideCharToMultiByte(CP_UTF8, 0, line.c_str(), (int)line.size(), nullptr, 0, nullptr, nullptr);
        std::string u(n, '\0');
        WideCharToMultiByte(CP_UTF8, 0, line.c_str(), (int)line.size(), &u[0], n, nullptr, nullptr);
        DWORD w; WriteFile(f, u.data(), (DWORD)u.size(), &w, nullptr);
    }
    CloseHandle(f);
    std::wstring dest = destIn;
    while (dest.size() > 3 && dest.back() == L'\\') dest.pop_back();
    std::wstring exe = DllDir() + L"\\SmartMove.exe";
    std::wstring cmd = L"\"" + exe + L"\" --job \"" + tmp + L"\" \"" + dest + L"\" " + (copy ? L"copy" : L"move");
    STARTUPINFOW si = {sizeof si}; PROCESS_INFORMATION pi = {};
    if (!CreateProcessW(exe.c_str(), &cmd[0], nullptr, nullptr, FALSE, 0, nullptr, DllDir().c_str(), &si, &pi)) { DeleteFileW(tmp); return false; }
    CloseHandle(pi.hThread); CloseHandle(pi.hProcess);
    return true;
}

// Scelta come in Esplora: Ctrl = copia, Maiusc = sposta, altrimenti stesso volume -> sposta, altro volume -> copia.
// Alt o Ctrl+Maiusc (collegamento) NON sono gestiti: il trascinamento viene rifiutato (vedi README).
static bool WantsLink(DWORD ks) { return (ks & MK_ALT) || ((ks & MK_CONTROL) && (ks & MK_SHIFT)); }
static bool ChooseCopy(DWORD ks, const std::wstring& first, const std::wstring& dest) {
    if (ks & MK_CONTROL) return true;
    if (ks & MK_SHIFT) return false;
    bool sameVol = first.size() > 1 && dest.size() > 1 && first[1] == L':' && dest[1] == L':' &&
                   towupper(first[0]) == towupper(dest[0]);
    return !sameVol;   // percorsi UNC: stessa regola (copia)
}

class SmartMoveDrop : public IDropTarget {
    long ref_ = 1;
    std::wstring dest_;
    bool hdrop_ = false;
    std::wstring first_;
public:
    explicit SmartMoveDrop(const std::wstring& dest) : dest_(dest) { InterlockedIncrement(&g_refs); }
    ~SmartMoveDrop() { InterlockedDecrement(&g_refs); }
    STDMETHODIMP QueryInterface(REFIID r, void** o) override {
        if (r == IID_IUnknown || r == IID_IDropTarget) { *o = static_cast<IDropTarget*>(this); AddRef(); return S_OK; }
        *o = nullptr; return E_NOINTERFACE;
    }
    STDMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&ref_); }
    STDMETHODIMP_(ULONG) Release() override { long r = InterlockedDecrement(&ref_); if (!r) delete this; return r; }

    void Effect(DWORD ks, DWORD* eff) {
        if (!hdrop_ || WantsLink(ks)) { *eff = DROPEFFECT_NONE; return; }
        *eff = ChooseCopy(ks, first_, dest_) ? DROPEFFECT_COPY : DROPEFFECT_MOVE;
    }
    STDMETHODIMP DragEnter(IDataObject* d, DWORD ks, POINTL, DWORD* eff) override {
        std::vector<std::wstring> v;
        hdrop_ = d && GetDropFiles(d, v);
        if (hdrop_) first_ = v[0];
        Effect(ks, eff); return S_OK;
    }
    STDMETHODIMP DragOver(DWORD ks, POINTL, DWORD* eff) override { Effect(ks, eff); return S_OK; }
    STDMETHODIMP DragLeave() override { return S_OK; }
    STDMETHODIMP Drop(IDataObject* d, DWORD ks, POINTL, DWORD* eff) override {
        try {
            std::vector<std::wstring> v;
            if (!d || WantsLink(ks) || !GetDropFiles(d, v)) { *eff = DROPEFFECT_NONE; return S_OK; }
            bool copy = ChooseCopy(ks, v[0], dest_);
            if (!LaunchJob(v, dest_, copy)) { *eff = DROPEFFECT_NONE; return S_OK; }
            // SICUREZZA: il motore sposta in modo asincrono. Se dichiarassimo MOVE, Esplora (origine) potrebbe
            // cancellare gli originali mentre copiamo. Dichiariamo quindi sempre COPY e segnaliamo
            // "PerformedDropEffect = NONE" (best effort); lo spostamento reale lo fa SmartMove.exe.
            UINT cf = RegisterClipboardFormatW(L"Performed DropEffect");
            FORMATETC fe = {(CLIPFORMAT)cf, nullptr, DVASPECT_CONTENT, -1, TYMED_HGLOBAL};
            STGMEDIUM sm = {}; sm.tymed = TYMED_HGLOBAL; sm.hGlobal = GlobalAlloc(GMEM_MOVEABLE, sizeof(DWORD));
            if (sm.hGlobal) { DWORD* p = (DWORD*)GlobalLock(sm.hGlobal); *p = DROPEFFECT_NONE; GlobalUnlock(sm.hGlobal);
                if (FAILED(d->SetData(&fe, &sm, TRUE))) GlobalFree(sm.hGlobal); }
            *eff = DROPEFFECT_COPY;
            return S_OK;
        } catch (...) { *eff = DROPEFFECT_NONE; return E_FAIL; }
    }
};

// IShellExtInit non serve: per DropHandler Explorer crea l'oggetto e chiede IPersistFile::Load(percorso cartella).
class SmartMoveDropObj : public IPersistFile, public IDropTarget {
    long ref_ = 1; std::wstring dest_; SmartMoveDrop* inner_ = nullptr;
public:
    SmartMoveDropObj() { InterlockedIncrement(&g_refs); }
    ~SmartMoveDropObj() { if (inner_) inner_->Release(); InterlockedDecrement(&g_refs); }
    STDMETHODIMP QueryInterface(REFIID r, void** o) override {
        if (r == IID_IUnknown || r == IID_IPersistFile || r == IID_IPersist) *o = static_cast<IPersistFile*>(this);
        else if (r == IID_IDropTarget) *o = static_cast<IDropTarget*>(this);
        else { *o = nullptr; return E_NOINTERFACE; }
        AddRef(); return S_OK;
    }
    STDMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&ref_); }
    STDMETHODIMP_(ULONG) Release() override { long r = InterlockedDecrement(&ref_); if (!r) delete this; return r; }
    STDMETHODIMP GetClassID(CLSID* c) override { *c = CLSID_SmartMoveDrop; return S_OK; }
    STDMETHODIMP IsDirty() override { return S_FALSE; }
    STDMETHODIMP Load(LPCOLESTR f, DWORD) override {
        dest_ = f ? f : L"";
        if (inner_) { inner_->Release(); inner_ = nullptr; }
        inner_ = new (std::nothrow) SmartMoveDrop(dest_);
        return inner_ ? S_OK : E_OUTOFMEMORY;
    }
    STDMETHODIMP Save(LPCOLESTR, BOOL) override { return E_NOTIMPL; }
    STDMETHODIMP SaveCompleted(LPCOLESTR) override { return E_NOTIMPL; }
    STDMETHODIMP GetCurFile(LPOLESTR*) override { return E_NOTIMPL; }
    STDMETHODIMP DragEnter(IDataObject* d, DWORD k, POINTL p, DWORD* e) override { return inner_ ? inner_->DragEnter(d, k, p, e) : E_UNEXPECTED; }
    STDMETHODIMP DragOver(DWORD k, POINTL p, DWORD* e) override { return inner_ ? inner_->DragOver(k, p, e) : E_UNEXPECTED; }
    STDMETHODIMP DragLeave() override { return inner_ ? inner_->DragLeave() : E_UNEXPECTED; }
    STDMETHODIMP Drop(IDataObject* d, DWORD k, POINTL p, DWORD* e) override { return inner_ ? inner_->Drop(d, k, p, e) : E_UNEXPECTED; }
};

template <class T> class TFactory : public IClassFactory {
    long ref_ = 1;
public:
    STDMETHODIMP QueryInterface(REFIID r, void** o) override {
        if (r == IID_IUnknown || r == IID_IClassFactory) { *o = static_cast<IClassFactory*>(this); AddRef(); return S_OK; }
        *o = nullptr; return E_NOINTERFACE;
    }
    STDMETHODIMP_(ULONG) AddRef() override { return InterlockedIncrement(&ref_); }
    STDMETHODIMP_(ULONG) Release() override { long r = InterlockedDecrement(&ref_); if (!r) delete this; return r; }
    STDMETHODIMP CreateInstance(IUnknown* outer, REFIID r, void** o) override {
        if (outer) return CLASS_E_NOAGGREGATION;
        auto* p = new (std::nothrow) T(); if (!p) return E_OUTOFMEMORY;
        HRESULT hr = p->QueryInterface(r, o); p->Release(); return hr;
    }
    STDMETHODIMP LockServer(BOOL l) override { if (l) InterlockedIncrement(&g_refs); else InterlockedDecrement(&g_refs); return S_OK; }
};

BOOL WINAPI DllMain(HINSTANCE h, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) { g_inst = h; DisableThreadLibraryCalls(h); }
    return TRUE;
}

extern "C" HRESULT WINAPI DllGetClassObject(REFCLSID c, REFIID r, void** o) {
    IClassFactory* f = nullptr;
    if (c == CLSID_SmartMoveMenu) f = new (std::nothrow) Factory();
    else if (c == CLSID_SmartMoveDrop) f = new (std::nothrow) TFactory<SmartMoveDropObj>();
    else return CLASS_E_CLASSNOTAVAILABLE;
    if (!f) return E_OUTOFMEMORY;
    HRESULT hr = f->QueryInterface(r, o); f->Release(); return hr;
}
extern "C" HRESULT WINAPI DllCanUnloadNow() { return g_refs == 0 ? S_OK : S_FALSE; }

// ---- registrazione per-utente (HKCU, niente admin) ----
static const wchar_t* kTargets[] = {L"Directory", L"Drive", L"Folder"};

static bool SetStr(const std::wstring& key, const wchar_t* name, const std::wstring& val) {
    HKEY k;
    if (RegCreateKeyExW(HKEY_CURRENT_USER, key.c_str(), 0, nullptr, 0, KEY_WRITE, nullptr, &k, nullptr) != ERROR_SUCCESS) return false;
    LONG r = RegSetValueExW(k, name, 0, REG_SZ, (const BYTE*)val.c_str(), (DWORD)((val.size() + 1) * sizeof(wchar_t)));
    RegCloseKey(k); return r == ERROR_SUCCESS;
}

extern "C" HRESULT WINAPI DllRegisterServer() {
    wchar_t p[MAX_PATH * 2]; GetModuleFileNameW(g_inst, p, MAX_PATH * 2);
    std::wstring base = std::wstring(L"Software\\Classes\\CLSID\\") + kClsidStr;
    bool ok = SetStr(base, nullptr, L"Smart Move drag handler") &&
              SetStr(base + L"\\InprocServer32", nullptr, p) &&
              SetStr(base + L"\\InprocServer32", L"ThreadingModel", L"Apartment");
    for (auto t : kTargets)
        ok = SetStr(std::wstring(L"Software\\Classes\\") + t + L"\\shellex\\DragDropHandlers\\SmartMove", nullptr, kClsidStr) && ok;
    SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, nullptr, nullptr);
    return ok ? S_OK : E_FAIL;
}
static const wchar_t* kDropClsid = L"{5B7F2C1A-8E34-4D6B-9A52-3C1D0E7F4A1A}";
// regsvr32 /n /i:drop SmartMoveShell.dll  -> registra anche il drop handler (opzionale, sperimentale)
// regsvr32 /u /n /i:drop SmartMoveShell.dll -> lo rimuove
extern "C" HRESULT WINAPI DllInstall(BOOL install, PCWSTR cmd) {
    if (!cmd || lstrcmpiW(cmd, L"drop") != 0) return E_INVALIDARG;
    const wchar_t* targets[] = {L"Directory", L"Drive"};
    if (!install) {
        for (auto t : targets) SHDeleteKeyW(HKEY_CURRENT_USER, (std::wstring(L"Software\\Classes\\") + t + L"\\shellex\\DropHandler").c_str());
        SHDeleteKeyW(HKEY_CURRENT_USER, (std::wstring(L"Software\\Classes\\CLSID\\") + kDropClsid).c_str());
        return S_OK;
    }
    wchar_t p[MAX_PATH * 2]; GetModuleFileNameW(g_inst, p, MAX_PATH * 2);
    std::wstring base = std::wstring(L"Software\\Classes\\CLSID\\") + kDropClsid;
    bool ok = SetStr(base, nullptr, L"Smart Move drop handler") && SetStr(base + L"\\InprocServer32", nullptr, p) &&
              SetStr(base + L"\\InprocServer32", L"ThreadingModel", L"Apartment");
    for (auto t : targets) ok = SetStr(std::wstring(L"Software\\Classes\\") + t + L"\\shellex\\DropHandler", nullptr, kDropClsid) && ok;
    SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, nullptr, nullptr);
    return ok ? S_OK : E_FAIL;
}

extern "C" HRESULT WINAPI DllUnregisterServer() {
    for (auto t : kTargets)
        SHDeleteKeyW(HKEY_CURRENT_USER, (std::wstring(L"Software\\Classes\\") + t + L"\\shellex\\DragDropHandlers\\SmartMove").c_str());
    SHDeleteKeyW(HKEY_CURRENT_USER, (std::wstring(L"Software\\Classes\\CLSID\\") + kClsidStr).c_str());
    DllInstall(FALSE, L"drop");
    SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, nullptr, nullptr);
    return S_OK;
}
