using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace SojaExiles
{
	/// <summary>
	/// Brick Project Studio 문·창문·서랍 스크립트용 입력 (NeonRider 수정)
	/// 원래 Input.GetMouseButtonDown(0)을 쓰던 부분을 New Input System에서도 동작하도록 교체
	/// </summary>
	public static class BPSInput
	{
		public static bool ClickDown()
		{
#if ENABLE_INPUT_SYSTEM
			return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
			return Input.GetMouseButtonDown(0);
#endif
		}
	}
}
