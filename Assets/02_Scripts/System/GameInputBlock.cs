using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 메뉴(콜 메뉴, ESC 메뉴 등)가 열려 있는 동안 게임 조작을 막는 공용 스위치.
/// 메뉴가 열릴 때 GameInputBlock.Push(this), 닫힐 때 Pop(this).
/// 조작 스크립트(오토바이, 탑승, 배달 상호작용)는 Blocked면 입력을 무시한다.
/// </summary>
public static class GameInputBlock
{
    static readonly HashSet<object> owners = new HashSet<object>();

    public static bool Blocked => owners.Count > 0;

    public static void Push(object owner) { if (owner != null) owners.Add(owner); }
    public static void Pop(object owner) { if (owner != null) owners.Remove(owner); }

    /// <summary>씬 재시작 등에서 남은 요청 정리</summary>
    public static void Clear() => owners.Clear();

    // 플레이 시작마다 초기화 (Domain Reload를 꺼둔 경우 대비)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetOnPlay() => owners.Clear();
}
