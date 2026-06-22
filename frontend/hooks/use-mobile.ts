import * as React from "react"

const MOBILE_BREAKPOINT = 768

// useSyncExternalStore is the React 19-recommended way to read a matchMedia value
// without a setState-in-effect (which triggers cascading renders).
function subscribe(onChange: () => void) {
  const mql = window.matchMedia(`(max-width: ${MOBILE_BREAKPOINT - 1}px)`)
  mql.addEventListener("change", onChange)
  return () => mql.removeEventListener("change", onChange)
}

export function useIsMobile() {
  return React.useSyncExternalStore(
    subscribe,
    () => window.innerWidth < MOBILE_BREAKPOINT,
    () => false,
  )
}
