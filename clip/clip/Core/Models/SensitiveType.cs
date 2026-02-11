namespace clip.Core.Models;

/// <summary>
/// 敏感信息类型枚举
/// </summary>
public enum SensitiveType
{
    None = 0,
    /// <summary>独立密码（复杂字符串）</summary>
    Password = 1,
    /// <summary>账号+密码组合</summary>
    Credential = 2,
    /// <summary>API Key / Token (sk-, ghp_, Bearer, AKIA...)</summary>
    ApiKey = 3,
    /// <summary>私钥 (PEM, SSH)</summary>
    PrivateKey = 4,
    /// <summary>数据库连接串</summary>
    ConnectionString = 5,
    /// <summary>其他敏感内容（手动标记）</summary>
    Other = 6
}
