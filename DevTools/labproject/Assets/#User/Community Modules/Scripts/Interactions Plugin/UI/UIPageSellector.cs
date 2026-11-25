using UnityEngine;

namespace CommunityModules
{
    public class UIPageSellector : MonoBehaviour
    {
        [SerializeField] GameObject[] _pages = new GameObject[0];
        [SerializeField] int _defaultPage = 0;

        private void Start()
        {
            SetPage(_defaultPage);
        }

        public void SetPage(int page)
        {
            for (int n = 0; n < _pages.Length; n++)
            {
                if (_pages[n] != null)
                {
                    if (page == n)
                        _pages[n].SetActive(true);
                    else
                        _pages[n].SetActive(false);
                }
            }
        }
    }
}
