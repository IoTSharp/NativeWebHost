using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace NativeWebHost.Windows.Win32;

/// <summary>使用 Windows 账户和 NetAPI 接口校验本机管理员成员身份。</summary>
internal static unsafe partial class WindowsAccountMembership
{
    private const uint MaximumPreferredLength = uint.MaxValue;
    private const uint IncludeIndirectMembership = 0x00000001;
    private const uint NetApiSuccess = 0;
    private const int ErrorInsufficientBuffer = 122;
    private const int ErrorNoneMapped = 1332;
    private const int SidTypeUser = 1;
    private const string BuiltinAdministratorsSid = "S-1-5-32-544";
    private const string LocalSystemSid = "S-1-5-18";
    private const string LocalServiceSid = "S-1-5-19";
    private const string NetworkServiceSid = "S-1-5-20";

    /// <summary>区分可接管的无效账户；原生查询故障继续抛出，禁止误换 owner。</summary>
    internal static WindowsUserSidValidationStatus ValidateLocalAdministrator(string userSid)
    {
        var securityIdentifier = new SecurityIdentifier(userSid);
        if (IsServiceAccount(securityIdentifier.Value))
        {
            return WindowsUserSidValidationStatus.ServiceAccount;
        }

        var userAccount = ResolveAccountName(securityIdentifier, allowUnmapped: true);
        if (userAccount is null)
        {
            return WindowsUserSidValidationStatus.AccountNotFound;
        }

        if (userAccount.SidType != SidTypeUser)
        {
            return WindowsUserSidValidationStatus.NotUserAccount;
        }

        var administratorsAccount = ResolveAccountName(
            new SecurityIdentifier(BuiltinAdministratorsSid),
            allowUnmapped: false)
            ?? throw new InvalidOperationException("无法解析 Windows 内置 Administrators 组。");
        if (!IsMemberOfLocalGroup(userAccount.QualifiedName, administratorsAccount.Name))
        {
            return WindowsUserSidValidationStatus.NotLocalAdministrator;
        }

        return WindowsUserSidValidationStatus.Valid;
    }

    /// <summary>按固定 SID 拒绝不会拥有交互式桌面的三个 Windows 服务账户。</summary>
    private static bool IsServiceAccount(string userSid)
        => string.Equals(userSid, LocalSystemSid, StringComparison.OrdinalIgnoreCase)
            || string.Equals(userSid, LocalServiceSid, StringComparison.OrdinalIgnoreCase)
            || string.Equals(userSid, NetworkServiceSid, StringComparison.OrdinalIgnoreCase);

    /// <summary>通过 SID 解析本地化账户名称，不依赖系统显示语言。</summary>
    private static ResolvedAccountName? ResolveAccountName(
        SecurityIdentifier securityIdentifier,
        bool allowUnmapped)
    {
        var sidBytes = new byte[securityIdentifier.BinaryLength];
        securityIdentifier.GetBinaryForm(sidBytes, 0);

        uint nameLength = 0;
        uint domainLength = 0;
        int sidType;
        fixed (byte* sidPointer = sidBytes)
        {
            var firstResult = LookupAccountSidW(
                null,
                sidPointer,
                null,
                ref nameLength,
                null,
                ref domainLength,
                out sidType);
            var firstError = Marshal.GetLastPInvokeError();
            if (firstResult == 0 && firstError != ErrorInsufficientBuffer)
            {
                if (allowUnmapped && firstError == ErrorNoneMapped)
                {
                    return null;
                }

                throw new Win32Exception(firstError, $"无法解析 Windows SID：{securityIdentifier.Value}");
            }

            if (nameLength == 0)
            {
                throw new InvalidOperationException($"Windows SID 没有可解析的账户名称：{securityIdentifier.Value}");
            }

            var nameBuffer = new char[checked((int)nameLength)];
            var domainBuffer = new char[Math.Max(1, checked((int)domainLength))];
            fixed (char* namePointer = nameBuffer)
            fixed (char* domainPointer = domainBuffer)
            {
                if (LookupAccountSidW(
                        null,
                        sidPointer,
                        namePointer,
                        ref nameLength,
                        domainPointer,
                        ref domainLength,
                        out sidType) == 0)
                {
                    var lookupError = Marshal.GetLastPInvokeError();
                    if (allowUnmapped && lookupError == ErrorNoneMapped)
                    {
                        return null;
                    }

                    throw new Win32Exception(
                        lookupError,
                        $"无法解析 Windows SID：{securityIdentifier.Value}");
                }
            }

            var name = new string(nameBuffer).TrimEnd('\0');
            var domain = new string(domainBuffer).TrimEnd('\0');
            var qualifiedName = string.IsNullOrWhiteSpace(domain)
                ? name
                : $"{domain}\\{name}";
            return new ResolvedAccountName(name, qualifiedName, sidType);
        }
    }

    /// <summary>使用 LG_INCLUDE_INDIRECT 查询用户的本地组，覆盖嵌套域组成员关系。</summary>
    private static bool IsMemberOfLocalGroup(string qualifiedUserName, string localGroupName)
    {
        var status = NetUserGetLocalGroups(
            null,
            qualifiedUserName,
            0,
            IncludeIndirectMembership,
            out var buffer,
            MaximumPreferredLength,
            out var entriesRead,
            out _);
        try
        {
            if (status != NetApiSuccess)
            {
                throw new Win32Exception(
                    unchecked((int)status),
                    $"无法查询 Windows 用户的本地组成员身份：{qualifiedUserName}");
            }

            // Level 0 的每个结构只有一个 LPWSTR 指针，可直接按指针数组遍历。
            var entries = (IntPtr*)buffer;
            for (uint index = 0; index < entriesRead; index++)
            {
                var groupName = Marshal.PtrToStringUni(entries[index]);
                if (string.Equals(groupName, localGroupName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                _ = NetApiBufferFree(buffer);
            }
        }
    }

    /// <summary>保存 SID 解析后的本地化名称、限定名称和账户类型。</summary>
    private sealed record ResolvedAccountName(string Name, string QualifiedName, int SidType);

    /// <summary>由 Windows SID 解析本地化账户名及域名。</summary>
    [LibraryImport("advapi32.dll", EntryPoint = "LookupAccountSidW", SetLastError = true)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    private static partial int LookupAccountSidW(
        char* systemName,
        byte* sid,
        char* name,
        ref uint nameLength,
        char* referencedDomainName,
        ref uint referencedDomainNameLength,
        out int sidType);

    /// <summary>查询用户所属的本地组名称列表。</summary>
    [LibraryImport("netapi32.dll", EntryPoint = "NetUserGetLocalGroups", StringMarshalling = StringMarshalling.Utf16)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    private static partial uint NetUserGetLocalGroups(
        string? serverName,
        string userName,
        uint level,
        uint flags,
        out IntPtr buffer,
        uint preferredMaximumLength,
        out uint entriesRead,
        out uint totalEntries);

    /// <summary>释放由 NetAPI 分配的成员列表缓冲区。</summary>
    [LibraryImport("netapi32.dll", EntryPoint = "NetApiBufferFree")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvStdcall)])]
    private static partial uint NetApiBufferFree(IntPtr buffer);
}
