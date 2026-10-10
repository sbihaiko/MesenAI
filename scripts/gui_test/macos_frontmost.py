#!/usr/bin/env python3
"""Which application is frontmost in this macOS session (#1255).

A GUI test run must never take the keyboard focus from whoever is using the
machine. `ShowActivated = false` and the accessory activation policy
(UI/Windows/TestHookActivation.cs) are how the application refrains from doing
it; this module is how the claim is checked for real: `frontmost_identifier()`
asks `NSWorkspace.frontmostApplication` - the same answer the window server
gives - so a test can read it before a launch and after it and see whether the
run took the front.

Read through ctypes against AppKit itself: no pyobjc, no extra dependency, and
no prompt for a permission. Reading the frontmost application is a read of the
caller's own session, which is why it can run unattended in a GUI test.

Off macOS there is no such notion, and `frontmost_identifier()` raises
`Unsupported` rather than answering something made up: `MESEN_GUI_WINDOW=any`
runs on Linux and in CI, where this check is skipped by its own test, not
silently passed."""
import ctypes
import sys

OBJC_LIBRARY = "/usr/lib/libobjc.dylib"
APPKIT_FRAMEWORK = "/System/Library/Frameworks/AppKit.framework/AppKit"


class Unsupported(RuntimeError):
    """This platform has no frontmost application to read."""


_handle = None


def _appkit():
    """The objc runtime with AppKit loaded, bound once per process."""
    global _handle
    if _handle is not None:
        return _handle
    if sys.platform != "darwin":
        raise Unsupported(f"the frontmost application is a macOS notion; this platform is {sys.platform!r}")
    # AppKit is loaded into the process first: NSWorkspace only exists once its
    # framework is in, and objc_getClass answers nil for a class nobody loaded.
    ctypes.CDLL(APPKIT_FRAMEWORK, mode=ctypes.RTLD_GLOBAL)
    objc = ctypes.CDLL(OBJC_LIBRARY)
    objc.objc_getClass.restype = ctypes.c_void_p
    objc.objc_getClass.argtypes = [ctypes.c_char_p]
    objc.sel_registerName.restype = ctypes.c_void_p
    objc.sel_registerName.argtypes = [ctypes.c_char_p]
    _handle = objc
    return objc


def _send(restype, *argtypes):
    """objc_msgSend retyped for one call: the ABI of a message depends on its
    signature, so the same symbol is bound again per call rather than once."""
    send = _appkit()["objc_msgSend"]
    send.restype = restype
    send.argtypes = list(argtypes)
    return send


def _class(name):
    return _appkit().objc_getClass(name.encode())


def _selector(name):
    return _appkit().sel_registerName(name.encode())


def _message(receiver, name):
    return _send(ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p)(receiver, _selector(name))


def _nsstring_to_str(pointer):
    """utf-8 of an NSString pointer, or None for nil."""
    if not pointer:
        return None
    text = _send(ctypes.c_char_p, ctypes.c_void_p, ctypes.c_void_p)(pointer, _selector("UTF8String"))
    if not text:
        return None
    return text.decode("utf-8", errors="replace")


def frontmost_identifier():
    """The frontmost application: its bundle id, or its name when it has none.

    The bundle identifier is what distinguishes two runs of the same
    application (`com.apple.Terminal` twice is still Terminal); the localized
    name is the fallback for a process that has no bundle at all, so the answer
    is never an empty string while an application is in front."""
    workspace = _message(_class("NSWorkspace"), "sharedWorkspace")
    if not workspace:
        raise Unsupported("NSWorkspace.sharedWorkspace is unavailable in this process")
    front = _message(workspace, "frontmostApplication")
    if not front:
        raise Unsupported("no application is frontmost right now")
    bundle = _nsstring_to_str(_message(front, "bundleIdentifier"))
    if bundle:
        return bundle
    name = _nsstring_to_str(_message(front, "localizedName"))
    if name:
        return name
    raise Unsupported("the frontmost application names neither a bundle nor a name")


#NSApplicationActivationPolicy (NSApplication.h). Accessory is what the run asks
#for: the process has windows, is not in the Dock, and never becomes the
#frontmost application - the numbers UI/Windows/TestHookActivation.cs sends.
REGULAR = 0
ACCESSORY = 1
PROHIBITED = 2

_application = None


def _shared_application():
    """This process's NSApplication, launched, bound once.

    The application is finished launching before anything is asked of it, which
    is the state the emulator is in when it sets its own policy: the hook's
    window is built during startup, after AppKit has launched the application
    and before the first window is shown."""
    global _application
    if _application is None:
        application = _message(_class("NSApplication"), "sharedApplication")
        if not application:
            raise Unsupported("NSApplication.sharedApplication is unavailable in this process")
        _send(None, ctypes.c_void_p, ctypes.c_void_p)(application, _selector("finishLaunching"))
        _application = application
    return _application


def activation_policy():
    """This process's NSApplicationActivationPolicy as AppKit reports it."""
    return _send(ctypes.c_long, ctypes.c_void_p, ctypes.c_void_p)(
        _shared_application(), _selector("activationPolicy"))


def set_activation_policy(policy):
    """Ask AppKit for an activation policy and return what it reports back.

    The same two calls TestHookActivation.SetAccessoryPolicy makes with the same
    argument type (NSInteger), so a wrong selector or a wrong argument width -
    the two ways a P/Invoke signature goes silently wrong - is visible here: a
    mismatch leaves the policy unchanged, and AppKit answers NO to the request."""
    accepted = _send(ctypes.c_bool, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_long)(
        _shared_application(), _selector("setActivationPolicy:"), policy)
    if not accepted:
        raise Unsupported(f"AppKit refused activation policy {policy} for this process")
    return activation_policy()
