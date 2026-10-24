// Blazor does not reliably report a change to the fragment alone, and the fragment is where
// every position lives — so the page listens for it itself, on both events that can carry one.
// Setting location.hash pushes a history entry, which is what makes the back button an undo.
export function listen(dotnet) {
    const handler = () => dotnet.invokeMethodAsync("OnFragmentChanged", location.hash);
    window.addEventListener("hashchange", handler);
    window.addEventListener("popstate", handler);
    return {
        dispose: () => {
            window.removeEventListener("hashchange", handler);
            window.removeEventListener("popstate", handler);
        },
    };
}

export function go(fragment) {
    location.hash = fragment;
}
