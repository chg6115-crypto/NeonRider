using UnityEngine;

/// <summary>
/// 비워 둬도 되는 참조 칸 표시. 검증 도구(Tools > NeonRider > Validate)가 빈 칸으로 보고하지 않는다.
/// 주석이나 Tooltip에 "비우면" / "비워도" / "(선택)"이 적힌 칸도 같은 취급이라, 이 표시는 그런 설명이 없을 때만 쓰면 된다.
/// 예) [OptionalRef] [SerializeField] Transform lookTarget;
/// </summary>
[System.AttributeUsage(System.AttributeTargets.Field)]
public class OptionalRefAttribute : PropertyAttribute { }
