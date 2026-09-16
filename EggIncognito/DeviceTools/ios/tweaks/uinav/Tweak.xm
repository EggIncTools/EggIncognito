#import <UIKit/UIKit.h>
#import <Foundation/Foundation.h>
#import <QuartzCore/QuartzCore.h>
#import <IOKit/IOKitLib.h>
#import <mach/mach.h>
#import <mach/mach_time.h>
#import <objc/message.h>
#import <dispatch/dispatch.h>
#import <notify.h>

static NSString *const kCmdPath = @"/tmp/egi-uinav.cmd";
static NSString *const kJsonPath = @"/tmp/egi-uinav.json";
static NSString *const kPngPath = @"/tmp/egi-uinav.png";
static NSString *const kDonePath = @"/tmp/egi-uinav.done";

static const int kMaxDepth = 60;
static const int kMaxNodes = 6000;

typedef double IOHIDFloat;
typedef struct __IOHIDEvent *IOHIDEventRef;
typedef struct __IOHIDEventSystemClient *IOHIDEventSystemClientRef;

extern "C" {
IOHIDEventRef IOHIDEventCreateDigitizerEvent(CFAllocatorRef allocator, uint64_t timeStamp,
    uint32_t type, uint32_t index, uint32_t identity, uint32_t eventMask, uint32_t buttonMask,
    IOHIDFloat x, IOHIDFloat y, IOHIDFloat z, IOHIDFloat tipPressure, IOHIDFloat twist,
    boolean_t range, boolean_t touch, IOOptionBits options);
IOHIDEventRef IOHIDEventCreateDigitizerFingerEvent(CFAllocatorRef allocator, uint64_t timeStamp,
    uint32_t index, uint32_t identity, uint32_t eventMask,
    IOHIDFloat x, IOHIDFloat y, IOHIDFloat z, IOHIDFloat tipPressure, IOHIDFloat twist,
    boolean_t range, boolean_t touch, IOOptionBits options);
void IOHIDEventAppendEvent(IOHIDEventRef parent, IOHIDEventRef child, IOOptionBits options);
IOHIDEventSystemClientRef IOHIDEventSystemClientCreate(CFAllocatorRef allocator);
void IOHIDEventSystemClientDispatchEvent(IOHIDEventSystemClientRef client, IOHIDEventRef event);
UIImage *_UICreateScreenUIImage(void);
}

#define kEgiHIDTransducerTypeHand 1
#define kEgiHIDDigitizerEventRange 0x00000001
#define kEgiHIDDigitizerEventTouch 0x00000002
#define kEgiHIDDigitizerEventPosition 0x00000004

static volatile int32_t gBusy = 0;
static dispatch_source_t gTimer;
static BOOL gIsSpringBoard = NO;

static const NSTimeInterval kSpringBoardDeferSeconds = 0.5;

static void writeDone(NSString *status) {
    @try {
        NSString *body = [status stringByAppendingString:@"\n"];
        NSData *d = [body dataUsingEncoding:NSUTF8StringEncoding];
        if (d) [d writeToFile:kDonePath atomically:YES];
    } @catch (__unused NSException *e) {
    }
}

static void clearOutputs(void) {
    NSFileManager *fm = [NSFileManager defaultManager];
    [fm removeItemAtPath:kDonePath error:nil];
}

static UIWindow *activeKeyWindow(void) {
    UIApplication *app = [UIApplication sharedApplication];
    if (!app) return nil;
    UIWindow *found = nil;
    @try {
        if ([app respondsToSelector:@selector(connectedScenes)]) {
            for (UIScene *scene in app.connectedScenes) {
                if (scene.activationState != UISceneActivationStateForegroundActive) continue;
                if (![scene isKindOfClass:[UIWindowScene class]]) continue;
                UIWindowScene *ws = (UIWindowScene *)scene;
                for (UIWindow *win in ws.windows) {
                    if (win.isKeyWindow) { found = win; break; }
                    if (!found && !win.hidden) found = win;
                }
                if (found) break;
            }
        }
    } @catch (__unused NSException *e) {
        found = nil;
    }
    if (!found) {
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wdeprecated-declarations"
        @try {
            found = app.keyWindow;
            if (!found) {
                for (UIWindow *win in app.windows) {
                    if (win.isKeyWindow) { found = win; break; }
                    if (!found && !win.hidden) found = win;
                }
            }
        } @catch (__unused NSException *e) {
            found = nil;
        }
#pragma clang diagnostic pop
    }
    return found;
}

static NSString *bestText(UIView *v) {
    @try {
        if ([v respondsToSelector:@selector(text)]) {
            id t = ((id(*)(id, SEL))objc_msgSend)(v, @selector(text));
            if ([t isKindOfClass:[NSString class]] && [(NSString *)t length] > 0) return (NSString *)t;
        }
        if ([v isKindOfClass:[UIButton class]]) {
            NSString *title = [(UIButton *)v currentTitle];
            if (title.length > 0) return title;
        }
    } @catch (__unused NSException *e) {
    }
    return nil;
}

static NSDictionary *frameDict(UIView *v) {
    CGRect r = CGRectZero;
    @try {
        CGRect inWindow = [v convertRect:v.bounds toView:nil];
        UIWindow *w = v.window;
        r = w ? [w convertRect:inWindow toWindow:nil] : inWindow;
    } @catch (__unused NSException *e) {
        r = v.frame;
    }
    return @{ @"x": @(r.origin.x), @"y": @(r.origin.y), @"w": @(r.size.width), @"h": @(r.size.height) };
}

static NSDictionary *nodeForView(UIView *v, int depth, int *count) {
    if (!v || depth > kMaxDepth || *count >= kMaxNodes) return nil;
    (*count)++;
    NSMutableDictionary *node = [NSMutableDictionary dictionary];
    @try {
        node[@"class"] = NSStringFromClass([v class]) ?: @"";
        NSString *label = nil;
        if ([v respondsToSelector:@selector(accessibilityLabel)]) label = v.accessibilityLabel;
        node[@"label"] = label ?: (id)[NSNull null];
        NSString *ident = nil;
        if ([v respondsToSelector:@selector(accessibilityIdentifier)]) ident = v.accessibilityIdentifier;
        node[@"id"] = ident ?: (id)[NSNull null];
        NSString *text = bestText(v);
        node[@"text"] = text ?: (id)[NSNull null];
        node[@"frame"] = frameDict(v);
        BOOL enabled = v.userInteractionEnabled && !v.hidden && v.alpha > 0.01;
        node[@"enabled"] = @(enabled);
        NSMutableArray *kids = [NSMutableArray array];
        for (UIView *sub in v.subviews) {
            NSDictionary *c = nodeForView(sub, depth + 1, count);
            if (c) [kids addObject:c];
            if (*count >= kMaxNodes) break;
        }
        node[@"children"] = kids;
    } @catch (__unused NSException *e) {
        node[@"class"] = node[@"class"] ?: @"<threw>";
        node[@"label"] = node[@"label"] ?: (id)[NSNull null];
        node[@"id"] = node[@"id"] ?: (id)[NSNull null];
        node[@"text"] = node[@"text"] ?: (id)[NSNull null];
        node[@"frame"] = node[@"frame"] ?: @{ @"x": @(0), @"y": @(0), @"w": @(0), @"h": @(0) };
        node[@"enabled"] = node[@"enabled"] ?: @(NO);
        node[@"children"] = node[@"children"] ?: @[];
    }
    return node;
}

static void doDump(void) {
    UIWindow *w = activeKeyWindow();
    if (!w) { writeDone(@"err no-key-window"); return; }
    int count = 0;
    NSDictionary *root = nodeForView(w, 0, &count);
    if (!root) { writeDone(@"err no-root"); return; }
    NSError *err = nil;
    NSData *json = [NSJSONSerialization dataWithJSONObject:root options:0 error:&err];
    if (!json) { writeDone([NSString stringWithFormat:@"err json %@", err.localizedDescription ?: @"?"]); return; }
    if (![json writeToFile:kJsonPath atomically:YES]) { writeDone(@"err write-json"); return; }
    writeDone([NSString stringWithFormat:@"ok dump nodes=%d", count]);
}

static void postDigitizerTouch(IOHIDFloat nx, IOHIDFloat ny, boolean_t touch) {
    uint32_t mask = kEgiHIDDigitizerEventRange | kEgiHIDDigitizerEventTouch | kEgiHIDDigitizerEventPosition;
    uint64_t ts = mach_absolute_time();
    IOHIDEventRef parent = IOHIDEventCreateDigitizerEvent(kCFAllocatorDefault, ts,
        kEgiHIDTransducerTypeHand, 0, 0, mask, 0,
        nx, ny, 0.0, touch ? 1.0 : 0.0, 0.0, 1, touch, 0);
    if (!parent) return;
    IOHIDEventRef finger = IOHIDEventCreateDigitizerFingerEvent(kCFAllocatorDefault, ts,
        1, 2, mask, nx, ny, 0.0, touch ? 1.0 : 0.0, 0.0, 1, touch, 0);
    if (finger) {
        IOHIDEventAppendEvent(parent, finger, 0);
        CFRelease(finger);
    }
    IOHIDEventSystemClientRef client = IOHIDEventSystemClientCreate(kCFAllocatorDefault);
    if (client) {
        IOHIDEventSystemClientDispatchEvent(client, parent);
        CFRelease((CFTypeRef)client);
    }
    CFRelease(parent);
}

static void doTap(double x, double y) {
    // STOPGAP: authored, needs on-device tuning. IOKit HID digitizer posting is the primary path but
    // IOHIDEventSystemClientDispatchEvent may require a specific IOHIDEventSystemClientCreate variant
    // and/or com.apple.private HID entitlements that differ per iOS version. Verify on the target
    // device that the tap actually lands. Documented fallback if HID posting is rejected: synthesize a
    // UITouch + UIEvent and feed it through the private _UIApplicationHandleEventFromQueueEvent path
    // (UIApplication-level touch synthesis).
    @try {
        CGRect screen = [UIScreen mainScreen].bounds;
        if (screen.size.width <= 0 || screen.size.height <= 0) { writeDone(@"err no-screen"); return; }
        IOHIDFloat nx = x / screen.size.width;
        IOHIDFloat ny = y / screen.size.height;
        if (nx < 0) nx = 0;
        if (nx > 1) nx = 1;
        if (ny < 0) ny = 0;
        if (ny > 1) ny = 1;
        postDigitizerTouch(nx, ny, 1);
        dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(0.05 * NSEC_PER_SEC)),
                       dispatch_get_main_queue(), ^{
            @try { postDigitizerTouch(nx, ny, 0); } @catch (__unused NSException *e) {}
        });
        writeDone(@"ok tap");
    } @catch (NSException *e) {
        writeDone([NSString stringWithFormat:@"err tap %@", e.reason ?: @"?"]);
    }
}

static UIResponder *firstResponderIn(UIView *root) {
    if (!root) return nil;
    @try {
        if ([root respondsToSelector:@selector(isFirstResponder)] && [root isFirstResponder]) return root;
        for (UIView *sub in root.subviews) {
            UIResponder *r = firstResponderIn(sub);
            if (r) return r;
        }
    } @catch (__unused NSException *e) {
    }
    return nil;
}

static void doText(NSString *payload) {
    if (payload.length == 0) { writeDone(@"err empty-text"); return; }
    @try {
        UIWindow *w = activeKeyWindow();
        if (!w) { writeDone(@"err no-key-window"); return; }
        UIResponder *r = firstResponderIn(w);
        if (r && [r respondsToSelector:@selector(insertText:)]) {
            ((void(*)(id, SEL, id))objc_msgSend)(r, @selector(insertText:), payload);
            writeDone(@"ok text");
        } else {
            writeDone(@"err no-first-responder");
        }
    } @catch (NSException *e) {
        writeDone([NSString stringWithFormat:@"err text %@", e.reason ?: @"?"]);
    }
}

static UIImage *captureViaPrivateScreenImage(void) {
    // STOPGAP: authored, needs on-device tuning. _UICreateScreenUIImage() is a private UIKit symbol
    // used only when the public UIGraphicsImageRenderer path yields nil. It may be absent or renamed on
    // some iOS versions; the public renderer above is the documented fallback and is the primary path.
    @try {
        return _UICreateScreenUIImage();
    } @catch (__unused NSException *e) {
        return nil;
    }
}

static UIImage *captureWindowImage(UIWindow *w) {
    if (!w) return nil;
    UIImage *img = nil;
    @try {
        CGRect b = w.bounds;
        if (b.size.width > 0 && b.size.height > 0) {
            UIGraphicsImageRendererFormat *fmt = [UIGraphicsImageRendererFormat preferredFormat];
            UIGraphicsImageRenderer *renderer = [[UIGraphicsImageRenderer alloc] initWithBounds:b format:fmt];
            img = [renderer imageWithActions:^(UIGraphicsImageRendererContext *ctx) {
                if (![w drawViewHierarchyInRect:b afterScreenUpdates:NO]) {
                    [w.layer renderInContext:ctx.CGContext];
                }
            }];
        }
    } @catch (__unused NSException *e) {
        img = nil;
    }
    if (!img) img = captureViaPrivateScreenImage();
    return img;
}

static void doScreenshot(void) {
    @try {
        UIWindow *w = activeKeyWindow();
        if (!w) { writeDone(@"err no-key-window"); return; }
        UIImage *img = captureWindowImage(w);
        if (!img) { writeDone(@"err no-image"); return; }
        NSData *png = UIImagePNGRepresentation(img);
        if (!png) { writeDone(@"err no-png"); return; }
        if (![png writeToFile:kPngPath atomically:YES]) { writeDone(@"err write-png"); return; }
        writeDone([NSString stringWithFormat:@"ok screenshot bytes=%lu", (unsigned long)png.length]);
    } @catch (NSException *e) {
        writeDone([NSString stringWithFormat:@"err screenshot %@", e.reason ?: @"?"]);
    }
}

static void doKeyHome(void) {
    // STOPGAP: authored, needs on-device tuning. -[UIApplication suspend] is a private selector that
    // backgrounds the foreground app (the visible effect of a home-button press). It may be a no-op or
    // rejected on some iOS versions. Documented fallbacks: post a Consumer-page HID menu event
    // (kHIDPage_Consumer / kHIDUsage_Csmr_Menu) via the same IOHIDEventSystemClient path as taps, or
    // dlopen SpringBoardServices and drive SBSuspend, mirroring eggupdate's SBSLockDevice usage.
    @try {
        UIApplication *app = [UIApplication sharedApplication];
        if (app && [app respondsToSelector:@selector(suspend)]) {
            ((void(*)(id, SEL))objc_msgSend)(app, @selector(suspend));
            writeDone(@"ok key home");
        } else {
            writeDone(@"err no-suspend");
        }
    } @catch (NSException *e) {
        writeDone([NSString stringWithFormat:@"err key %@", e.reason ?: @"?"]);
    }
}

static bool readNotifyState(const char *key, uint64_t *out) {
    int token = 0;
    if (notify_register_check(key, &token) != NOTIFY_STATUS_OK) return false;
    uint64_t state = 0;
    int rc = notify_get_state(token, &state);
    notify_cancel(token);
    if (rc != NOTIFY_STATUS_OK) return false;
    *out = state;
    return true;
}

static void doState(void) {
    @try {
        NSString *awake = @"unknown";
        NSString *locked = @"unknown";
        uint64_t v = 0;
        if (readNotifyState("com.apple.iokit.hid.displayStatus", &v)) awake = v ? @"1" : @"0";
        else if (readNotifyState("com.apple.springboard.hasBlankedScreen", &v)) awake = v ? @"0" : @"1";
        if (readNotifyState("com.apple.springboard.lockstate", &v)) locked = v ? @"1" : @"0";
        writeDone([NSString stringWithFormat:@"ok state awake=%@ locked=%@", awake, locked]);
    } @catch (NSException *e) {
        writeDone([NSString stringWithFormat:@"err state %@", e.reason ?: @"?"]);
    }
}

static void doFrontmost(void) {
    @try {
        NSString *bundle = [[NSBundle mainBundle] bundleIdentifier] ?: @"";
        if ([bundle caseInsensitiveCompare:@"com.apple.springboard"] == NSOrderedSame) bundle = @"";
        writeDone([NSString stringWithFormat:@"ok frontmost bundle=%@", bundle]);
    } @catch (NSException *e) {
        writeDone([NSString stringWithFormat:@"err frontmost %@", e.reason ?: @"?"]);
    }
}

static void dispatchCommand(NSString *line) {
    if (line.length == 0) { writeDone(@"err empty-command"); return; }
    if ([line hasPrefix:@"text "]) {
        doText([line substringFromIndex:5]);
        return;
    }
    NSArray<NSString *> *parts =
        [line componentsSeparatedByCharactersInSet:[NSCharacterSet whitespaceCharacterSet]];
    NSMutableArray<NSString *> *tok = [NSMutableArray array];
    for (NSString *p in parts) { if (p.length > 0) [tok addObject:p]; }
    if (tok.count == 0) { writeDone(@"err empty-command"); return; }
    NSString *verb = tok[0];
    if ([verb isEqualToString:@"dump"]) {
        doDump();
    } else if ([verb isEqualToString:@"state"]) {
        doState();
    } else if ([verb isEqualToString:@"frontmost"]) {
        doFrontmost();
    } else if ([verb isEqualToString:@"tap"]) {
        if (tok.count >= 3) doTap([tok[1] doubleValue], [tok[2] doubleValue]);
        else writeDone(@"err tap-args");
    } else if ([verb isEqualToString:@"screenshot"]) {
        doScreenshot();
    } else if ([verb isEqualToString:@"key"]) {
        if (tok.count >= 2 && [tok[1] isEqualToString:@"home"]) doKeyHome();
        else writeDone(@"err key-args");
    } else if ([verb isEqualToString:@"text"]) {
        writeDone(@"err empty-text");
    } else {
        writeDone([NSString stringWithFormat:@"err unknown-command %@", verb]);
    }
}

static void handleOnMain(void) {
    UIApplication *app = [UIApplication sharedApplication];
    if (!app || app.applicationState != UIApplicationStateActive) return;
    NSFileManager *fm = [NSFileManager defaultManager];
    NSString *content = [NSString stringWithContentsOfFile:kCmdPath encoding:NSUTF8StringEncoding error:nil];
    if (!content) return;
    [fm removeItemAtPath:kCmdPath error:nil];
    NSRange nl = [content rangeOfString:@"\n"];
    NSString *line = nl.location == NSNotFound ? content : [content substringToIndex:nl.location];
    line = [line stringByTrimmingCharactersInSet:[NSCharacterSet characterSetWithCharactersInString:@"\r\n"]];
    NSString *trimmed = [line stringByTrimmingCharactersInSet:[NSCharacterSet whitespaceCharacterSet]];
    if (trimmed.length == 0) return;
    clearOutputs();
    dispatchCommand(line);
}

static BOOL springBoardShouldDefer(void) {
    if (!gIsSpringBoard) return NO;
    NSDictionary *attrs = [[NSFileManager defaultManager] attributesOfItemAtPath:kCmdPath error:nil];
    NSDate *written = attrs[NSFileModificationDate];
    if (!written) return YES;
    return [[NSDate date] timeIntervalSinceDate:written] < kSpringBoardDeferSeconds;
}

static void pollCommand(void) {
    @try {
        if (![[NSFileManager defaultManager] fileExistsAtPath:kCmdPath]) return;
        if (springBoardShouldDefer()) return;
        if (!__sync_bool_compare_and_swap(&gBusy, 0, 1)) return;
        dispatch_async(dispatch_get_main_queue(), ^{
            @try {
                handleOnMain();
            } @catch (NSException *e) {
                writeDone([NSString stringWithFormat:@"err exception %@", e.reason ?: @"?"]);
            } @finally {
                gBusy = 0;
            }
        });
    } @catch (__unused NSException *e) {
        gBusy = 0;
    }
}

static void startWatcher(void) {
    dispatch_queue_t q = dispatch_queue_create("com.eggincognito.uinav.watch", DISPATCH_QUEUE_SERIAL);
    gTimer = dispatch_source_create(DISPATCH_SOURCE_TYPE_TIMER, 0, 0, q);
    dispatch_source_set_timer(gTimer, dispatch_time(DISPATCH_TIME_NOW, 0),
                              (uint64_t)(0.2 * NSEC_PER_SEC), (uint64_t)(0.05 * NSEC_PER_SEC));
    dispatch_source_set_event_handler(gTimer, ^{ pollCommand(); });
    dispatch_resume(gTimer);
}

%ctor {
    @autoreleasepool {
        @try {
            NSString *host = [[NSBundle mainBundle] bundleIdentifier];
            gIsSpringBoard = [host caseInsensitiveCompare:@"com.apple.springboard"] == NSOrderedSame;
            startWatcher();
        } @catch (__unused NSException *e) {
        }
    }
}
