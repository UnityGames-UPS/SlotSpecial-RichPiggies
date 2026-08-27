using UnityEngine;
using UnityEngine.UI;

public class SymbolButtonHandler : MonoBehaviour
{
    private int columnIndex;
    private int rowIndex;
    private SlotView slotView;
    private Button button;

    public void Init(int col, int row, SlotView view)
    {
        columnIndex = col;
        rowIndex = row;
        slotView = view;

        button = GetComponent<Button>();
        if (button == null)
        {
            button = gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
        }

        button.onClick.RemoveListener(OnClick);
        button.onClick.AddListener(OnClick);
    }

    private void OnClick()
    {
        if (slotView != null)
        {
            slotView.OnSymbolClicked(columnIndex, rowIndex, transform as RectTransform);
        }
    }
}
