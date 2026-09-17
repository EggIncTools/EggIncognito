#import <Foundation/Foundation.h>
#import <dispatch/dispatch.h>
#import <sys/event.h>
#import <fcntl.h>
#import <objc/runtime.h>
#import <objc/message.h>
#import <dlfcn.h>

static NSString *const kTriggerPath = @"/var/mobile/eggupdate.trigger";
static NSString *const kLogPath = @"/var/mobile/eggupdate.log";
static const long long kEggIncAdamId = 993492744;

#ifndef EGGUPDATE_ARMED
#define EGGUPDATE_ARMED 0
#endif

static void egglog(NSString *fmt, ...) {
    va_list ap; va_start(ap, fmt);
    NSString *msg = [[NSString alloc] initWithFormat:fmt arguments:ap];
    va_end(ap);
    NSString *line = [NSString stringWithFormat:@"%@ %@\n", [NSDate date], msg];
    FILE *f = fopen([kLogPath fileSystemRepresentation], "a");
    if (f) { fwrite([line UTF8String], 1, strlen([line UTF8String]), f); fclose(f); }
}

static void dumpAllMethods(id obj, NSString *label) {
    if (!obj) { egglog(@"  %@ = nil", label); return; }
    Class c = object_getClass(obj);
    egglog(@"  %@ class = %s", label, class_getName(c));
    unsigned int n = 0;
    Method *methods = class_copyMethodList(c, &n);
    for (unsigned int i = 0; i < n; i++) {
        egglog(@"    -[%s %s]", class_getName(c), sel_getName(method_getName(methods[i])));
    }
    free(methods);
}

static void dumpAllIvars(id obj, NSString *label) {
    if (!obj) { egglog(@"  %@ ivars = nil", label); return; }
    Class c = object_getClass(obj);
    egglog(@"  %@ ivars (class %s):", label, class_getName(c));
    unsigned int n = 0;
    Ivar *ivars = class_copyIvarList(c, &n);
    for (unsigned int i = 0; i < n; i++) {
        const char *name = ivar_getName(ivars[i]);
        const char *type = ivar_getTypeEncoding(ivars[i]);
        if (type && type[0] == '@') {
            @try {
                id v = object_getIvar(obj, ivars[i]);
                egglog(@"    %s : %@", name, v ? NSStringFromClass([v class]) : @"nil");
            } @catch (NSException *e) { egglog(@"    %s : <threw>", name); }
        } else {
            egglog(@"    %s : (scalar %s)", name, type ? type : "?");
        }
    }
    free(ivars);
}

static void fetchUpdatesThen(void (^then)(id eggUpdate, NSArray *allUpdates)) {
    Class ASDUpdatesService = NSClassFromString(@"ASDUpdatesService");
    if (!ASDUpdatesService) { egglog(@"ASDUpdatesService not found in this process"); then(nil, nil); return; }

    id svc = ((id(*)(id, SEL))objc_msgSend)(ASDUpdatesService, sel_registerName("defaultService"));
    if (!svc) { egglog(@"ASDUpdatesService defaultService = nil"); then(nil, nil); return; }

    dumpAllMethods(svc, @"ASDUpdatesService");

    if ([svc respondsToSelector:sel_registerName("hasEntitlement")]) {
        BOOL ent = ((BOOL(*)(id, SEL))objc_msgSend)(svc, sel_registerName("hasEntitlement"));
        egglog(@"ASDUpdatesService hasEntitlement = %d", ent);
    }
    if ([svc respondsToSelector:sel_registerName("autoUpdateEnabled")]) {
        BOOL au = ((BOOL(*)(id, SEL))objc_msgSend)(svc, sel_registerName("autoUpdateEnabled"));
        egglog(@"ASDUpdatesService autoUpdateEnabled = %d", au);
    }

    SEL getSel = sel_registerName("getUpdatesWithCompletionBlock:");
    if (![svc respondsToSelector:getSel]) {
        egglog(@"svc has no getUpdatesWithCompletionBlock:");
        then(nil, nil);
        return;
    }

    void (^readUpdates)(void) = ^{
        egglog(@"phase 1: getUpdatesWithCompletionBlock: ...");
        void (^completion)(id) = ^(id updates) {
            @autoreleasepool {
                NSArray *list = [updates isKindOfClass:[NSArray class]] ? (NSArray *)updates : nil;
                egglog(@"phase 1 callback: %lu updates", (unsigned long)list.count);
                id egg = nil;
                for (id u in list) {
                    long long adam = 0;
                    if ([u respondsToSelector:sel_registerName("itemIdentifier")])
                        adam = ((long long(*)(id, SEL))objc_msgSend)(u, sel_registerName("itemIdentifier"));
                    else if ([u respondsToSelector:sel_registerName("adamID")])
                        adam = ((long long(*)(id, SEL))objc_msgSend)(u, sel_registerName("adamID"));
                    egglog(@"  update adamID=%lld", adam);
                    if (adam == kEggIncAdamId) egg = u;
                }
                then(egg, list);
            }
        };
        ((void(*)(id, SEL, id))objc_msgSend)(svc, getSel, completion);
    };

    SEL reloadSel = sel_registerName("reloadFromServerWithCompletionBlock:");
    if ([svc respondsToSelector:reloadSel]) {
        egglog(@"phase 0: reloadFromServerWithCompletionBlock: ...");
        void (^reloadDone)(id arg) = ^(id arg) {
            @autoreleasepool {
                egglog(@"reloadFromServer callback returned arg=%@ (class %@); reading updates",
                       arg, arg ? NSStringFromClass([arg class]) : @"nil");
                readUpdates();
            }
        };
        ((void(*)(id, SEL, id))objc_msgSend)(svc, reloadSel, reloadDone);
    } else {
        egglog(@"no reloadFromServerWithCompletionBlock:; reading cached updates directly");
        readUpdates();
    }
}

static void installUpdate(id eggUpdate) {
    if (!eggUpdate) { egglog(@"no egginc update in pending list (downgrade + uiopen nudge may be needed)"); return; }
    egglog(@"egginc update object found; methods follow (pick phase-2 selector from these):");
    dumpAllMethods(eggUpdate, @"eggUpdate");

#if EGGUPDATE_ARMED
    egglog(@"EGGUPDATE_ARMED set but no confirmed install selector wired yet; aborting install");
#else
    egglog(@"EGGUPDATE_ARMED=0; phase-2 install withheld (safe). Re-build with -DEGGUPDATE_ARMED=1 after confirming selector.");
#endif
}

__attribute__((unused)) static void probeServiceBroker(void) {
    Class Broker = NSClassFromString(@"ASDServiceBroker");
    if (!Broker) { egglog(@"ASDServiceBroker not found in this process"); return; }
    egglog(@"ASDServiceBroker found; enumerating");

    id broker = nil;
    const char *accessors[] = {"sharedInstance", "defaultBroker", "sharedBroker", "broker"};
    for (unsigned int ai = 0; ai < sizeof(accessors)/sizeof(accessors[0]); ai++) {
        const char *acc = accessors[ai];
        SEL s = sel_registerName(acc);
        if ([Broker respondsToSelector:s]) {
            broker = ((id(*)(id, SEL))objc_msgSend)(Broker, s);
            egglog(@"  +[ASDServiceBroker %s] -> %@", acc, broker);
            if (broker) break;
        }
    }
    if (!broker) {
        @try { broker = ((id(*)(id, SEL))objc_msgSend)((id)Broker, sel_registerName("alloc"));
               broker = ((id(*)(id, SEL))objc_msgSend)(broker, sel_registerName("init"));
               egglog(@"  alloc/init broker -> %@", broker); }
        @catch (NSException *e) { egglog(@"  alloc/init broker threw %@", e.reason); }
    }
    if (!broker) { egglog(@"  could not obtain an ASDServiceBroker instance"); return; }
    dumpAllMethods(broker, @"ASDServiceBroker");

    SEL syncSel = sel_registerName("getUpdatesServiceWithError:");
    id brokerSvc = nil;
    if ([broker respondsToSelector:syncSel]) {
        NSError *err = nil;
        @try {
            brokerSvc = ((id(*)(id, SEL, NSError**))objc_msgSend)(broker, syncSel, &err);
            egglog(@"  getUpdatesServiceWithError: -> %@ (err=%@)", brokerSvc, err);
        } @catch (NSException *e) { egglog(@"  getUpdatesServiceWithError: threw %@", e.reason); }
    }
    if (!brokerSvc) { egglog(@"  broker did not vend an updates service synchronously"); return; }

    dumpAllIvars(brokerSvc, @"broker-vended distant object");
    @try {
        id conn = nil;
        if ([brokerSvc respondsToSelector:sel_registerName("_connection")])
            conn = ((id(*)(id, SEL))objc_msgSend)(brokerSvc, sel_registerName("_connection"));
        egglog(@"  distant-object _connection = %@ (class %@)", conn, conn ? NSStringFromClass([conn class]) : @"nil");
        if (conn) dumpAllIvars(conn, @"entitled NSXPCConnection");
    } @catch (NSException *e) { egglog(@"  _connection probe threw %@", e.reason); }

    Class ASDU = NSClassFromString(@"ASDUpdatesService");
    id localSvc = ASDU ? ((id(*)(id, SEL))objc_msgSend)(ASDU, sel_registerName("defaultService")) : nil;
    dumpAllIvars(localSvc, @"local ASDUpdatesService singleton");

    if (localSvc) {
        Ivar entIvar = class_getInstanceVariable(ASDU, "_hasUpdatesEntitlement");
        if (entIvar) {
            ptrdiff_t off = ivar_getOffset(entIvar);
            BOOL *flag = (BOOL *)((char *)(__bridge void *)localSvc + off);
            egglog(@"  _hasUpdatesEntitlement before flip = %d", *flag);
            *flag = YES;
            BOOL after = ((BOOL(*)(id, SEL))objc_msgSend)(localSvc, sel_registerName("hasEntitlement"));
            egglog(@"  _hasUpdatesEntitlement after flip, hasEntitlement() = %d", after);

            egglog(@"  [flipped-local] phase 0: reloadFromServerWithCompletionBlock: ...");
            void (^getAfter)(void) = ^{
                void (^cb)(id) = ^(id updates) {
                    @autoreleasepool {
                        NSArray *list = [updates isKindOfClass:[NSArray class]] ? (NSArray *)updates : nil;
                        egglog(@"  [flipped-local] getUpdates callback: %lu updates", (unsigned long)list.count);
                        for (id u in list) {
                            long long adam = 0;
                            if ([u respondsToSelector:sel_registerName("itemIdentifier")])
                                adam = ((long long(*)(id, SEL))objc_msgSend)(u, sel_registerName("itemIdentifier"));
                            egglog(@"  [flipped-local] update adamID=%lld", adam);
                        }
                    }
                };
                ((void(*)(id, SEL, id))objc_msgSend)(localSvc, sel_registerName("getUpdatesWithCompletionBlock:"), cb);
            };
            void (^reloadCb)(id) = ^(id arg) {
                @autoreleasepool {
                    egglog(@"  [flipped-local] reload callback arg=%@ (class %@)",
                           arg, arg ? NSStringFromClass([arg class]) : @"nil");
                    getAfter();
                }
            };
            ((void(*)(id, SEL, id))objc_msgSend)(localSvc, sel_registerName("reloadFromServerWithCompletionBlock:"), reloadCb);
        } else {
            egglog(@"  _hasUpdatesEntitlement ivar not found");
        }
    }

    SEL reloadSel = sel_registerName("reloadFromServerWithCompletionBlock:");
    SEL getSel = sel_registerName("getUpdatesWithCompletionBlock:");
    void (^readVia)(void) = ^{
        egglog(@"  [broker-svc] phase 1: getUpdatesWithCompletionBlock: ...");
        void (^cb)(id) = ^(id updates) {
            @autoreleasepool {
                NSArray *list = [updates isKindOfClass:[NSArray class]] ? (NSArray *)updates : nil;
                egglog(@"  [broker-svc] getUpdates callback: %lu updates", (unsigned long)list.count);
                for (id u in list) {
                    long long adam = 0;
                    if ([u respondsToSelector:sel_registerName("itemIdentifier")])
                        adam = ((long long(*)(id, SEL))objc_msgSend)(u, sel_registerName("itemIdentifier"));
                    else if ([u respondsToSelector:sel_registerName("adamID")])
                        adam = ((long long(*)(id, SEL))objc_msgSend)(u, sel_registerName("adamID"));
                    egglog(@"  [broker-svc] update adamID=%lld", adam);
                }
            }
        };
        @try { ((void(*)(id, SEL, id))objc_msgSend)(brokerSvc, getSel, cb); }
        @catch (NSException *e) { egglog(@"  [broker-svc] getUpdates threw %@", e.reason); }
    };
    egglog(@"  [broker-svc] phase 0: reloadFromServerWithCompletionBlock: ...");
    void (^reloadDone)(id) = ^(id arg) {
        @autoreleasepool {
            egglog(@"  [broker-svc] reload callback arg=%@ (class %@)",
                   arg, arg ? NSStringFromClass([arg class]) : @"nil");
            readVia();
        }
    };
    @try { ((void(*)(id, SEL, id))objc_msgSend)(brokerSvc, reloadSel, reloadDone); }
    @catch (NSException *e) { egglog(@"  [broker-svc] reload threw %@; reading directly", e.reason); readVia(); }
}

static void logUpdateList(NSString *tag, id updates) {
    NSArray *list = [updates isKindOfClass:[NSArray class]] ? (NSArray *)updates : nil;
    egglog(@"  [%@] -> %lu items (raw class %@)", tag, (unsigned long)list.count,
           updates ? NSStringFromClass([updates class]) : @"nil");
    for (id u in list) {
        long long adam = 0;
        if ([u respondsToSelector:sel_registerName("itemIdentifier")])
            adam = ((long long(*)(id, SEL))objc_msgSend)(u, sel_registerName("itemIdentifier"));
        else if ([u respondsToSelector:sel_registerName("adamID")])
            adam = ((long long(*)(id, SEL))objc_msgSend)(u, sel_registerName("adamID"));
        egglog(@"  [%@]   item adamID=%lld class=%@", tag, adam, NSStringFromClass([u class]));
    }
}

static void readerSync(id svc, const char *selName) {
    SEL s = sel_registerName(selName);
    if (![svc respondsToSelector:s]) { egglog(@"  %s: not responded", selName); return; }
    NSString *tag = [NSString stringWithUTF8String:selName];
    dispatch_semaphore_t sem = dispatch_semaphore_create(0);
    void (^cb)(id) = ^(id updates) { @autoreleasepool { logUpdateList(tag, updates); dispatch_semaphore_signal(sem); } };
    @try {
        ((void(*)(id, SEL, id))objc_msgSend)(svc, s, cb);
        if (dispatch_semaphore_wait(sem, dispatch_time(DISPATCH_TIME_NOW, (int64_t)(12 * NSEC_PER_SEC))) != 0)
            egglog(@"  %s: TIMEOUT (no reply in 12s)", selName);
    } @catch (NSException *e) { egglog(@"  %s threw %@", selName, e.reason); }
}

__attribute__((unused)) static void probeModernUpdates(void) {
    dispatch_async(dispatch_get_global_queue(DISPATCH_QUEUE_PRIORITY_DEFAULT, 0), ^{
        @autoreleasepool {
            Class ASDU = NSClassFromString(@"ASDUpdatesService");
            if (!ASDU) { egglog(@"probeModern: no ASDUpdatesService"); return; }
            id svc = ((id(*)(id, SEL))objc_msgSend)(ASDU, sel_registerName("defaultService"));
            if (!svc) { egglog(@"probeModern: nil defaultService"); return; }
            egglog(@"probeModern: hasEntitlement=%d (this process)",
                   ((BOOL(*)(id, SEL))objc_msgSend)(svc, sel_registerName("hasEntitlement")));

            SEL modSel = sel_registerName("shouldUseModernUpdatesWithCompletionBlock:");
            if ([svc respondsToSelector:modSel]) {
                dispatch_semaphore_t sm = dispatch_semaphore_create(0);
                void (^cb)(id) = ^(id v) { egglog(@"  shouldUseModernUpdates -> %@", v); dispatch_semaphore_signal(sm); };
                @try { ((void(*)(id, SEL, id))objc_msgSend)(svc, modSel, cb);
                       dispatch_semaphore_wait(sm, dispatch_time(DISPATCH_TIME_NOW, (int64_t)(8*NSEC_PER_SEC))); }
                @catch (NSException *e) { egglog(@"  shouldUseModernUpdates threw %@", e.reason); }
            }

            SEL bgReload = sel_registerName("reloadFromServerInBackgroundWithCompletionBlock:");
            if ([svc respondsToSelector:bgReload]) {
                egglog(@"  reloadFromServerInBackground ...");
                dispatch_semaphore_t sr = dispatch_semaphore_create(0);
                void (^rcb)(id) = ^(id arg) {
                    egglog(@"  bgReload cb arg=%@ (class %@)", arg, arg ? NSStringFromClass([arg class]) : @"nil");
                    dispatch_semaphore_signal(sr);
                };
                @try { ((void(*)(id, SEL, id))objc_msgSend)(svc, bgReload, rcb);
                       if (dispatch_semaphore_wait(sr, dispatch_time(DISPATCH_TIME_NOW, (int64_t)(12*NSEC_PER_SEC))) != 0)
                           egglog(@"  bgReload: TIMEOUT"); }
                @catch (NSException *e) { egglog(@"  bgReload threw %@", e.reason); }
            }
            readerSync(svc, "getUpdatesWithCompletionBlock:");
            readerSync(svc, "getUpdatesIncludingMetricsWithCompletionBlock:");
            readerSync(svc, "getManagedUpdatesWithCompletionBlock:");
            egglog(@"probeModern: done");
        }
    });
}

static void dumpProtocolMethods(Protocol *p, NSString *label) {
    if (!p) { egglog(@"  %@ protocol = nil", label); return; }
    egglog(@"  %@ protocol = %s", label, protocol_getName(p));
    for (int req = 0; req < 2; req++) {
        for (int inst = 0; inst < 2; inst++) {
            unsigned int n = 0;
            struct objc_method_description *md =
                protocol_copyMethodDescriptionList(p, req == 0, inst == 1, &n);
            for (unsigned int i = 0; i < n; i++)
                egglog(@"    %@%@ %s", req == 0 ? @"@req " : @"@opt ", inst == 1 ? @"-" : @"+",
                       sel_getName(md[i].name));
            if (md) free(md);
        }
    }
}

static void probeRemoteProtocol(void) {
    Class Broker = NSClassFromString(@"ASDServiceBroker");
    if (!Broker) { egglog(@"remoteProto: no ASDServiceBroker"); return; }
    id broker = nil;
    if ([Broker respondsToSelector:sel_registerName("defaultBroker")])
        broker = ((id(*)(id, SEL))objc_msgSend)(Broker, sel_registerName("defaultBroker"));
    if (!broker) { egglog(@"remoteProto: no broker instance"); return; }
    id svc = nil;
    SEL syncSel = sel_registerName("getUpdatesServiceWithError:");
    if ([broker respondsToSelector:syncSel]) {
        NSError *err = nil;
        @try { svc = ((id(*)(id, SEL, NSError**))objc_msgSend)(broker, syncSel, &err); }
        @catch (NSException *e) { egglog(@"  getUpdatesService threw %@", e.reason); }
    }
    if (!svc) { egglog(@"remoteProto: no distant object"); return; }

    Ivar ri = class_getInstanceVariable(object_getClass(svc), "_remoteInterface");
    id iface = ri ? object_getIvar(svc, ri) : nil;
    egglog(@"  _remoteInterface = %@ (class %@)", iface, iface ? NSStringFromClass([iface class]) : @"nil");
    if (iface && [iface respondsToSelector:sel_registerName("protocol")]) {
        Protocol *p = ((Protocol*(*)(id, SEL))objc_msgSend)(iface, sel_registerName("protocol"));
        dumpProtocolMethods(p, @"updates-service remote");
    }
    dumpAllMethods(svc, @"distant-object proxy");
}

static void probeEntitledRemote(void) {
    dispatch_async(dispatch_get_global_queue(DISPATCH_QUEUE_PRIORITY_DEFAULT, 0), ^{
        @autoreleasepool {
            Class Broker = NSClassFromString(@"ASDServiceBroker");
            if (!Broker) { egglog(@"entitledRemote: no broker class"); return; }
            id broker = ((id(*)(id, SEL))objc_msgSend)(Broker, sel_registerName("defaultBroker"));
            if (!broker) { egglog(@"entitledRemote: no broker"); return; }
            NSError *verr = nil;
            id svc = ((id(*)(id, SEL, NSError**))objc_msgSend)(broker, sel_registerName("getUpdatesServiceWithError:"), &verr);
            if (!svc) { egglog(@"entitledRemote: vend failed err=%@", verr); return; }

            id proxy = svc;
            SEL ehSel = sel_registerName("remoteObjectProxyWithErrorHandler:");
            if ([svc respondsToSelector:ehSel]) {
                void (^eh)(NSError *) = ^(NSError *e) { egglog(@"  [entitled] XPC error: %@", e); };
                @try { proxy = ((id(*)(id, SEL, id))objc_msgSend)(svc, ehSel, eh); }
                @catch (NSException *ex) { egglog(@"  proxyWithErrorHandler threw %@", ex.reason); proxy = svc; }
            }

            dispatch_semaphore_t s1 = dispatch_semaphore_create(0);
            void (^reloadReply)(id, id) = ^(id a, id b) {
                egglog(@"  [entitled] reload reply a=%@ b=%@", a, b); dispatch_semaphore_signal(s1);
            };
            egglog(@"  [entitled] reloadFromServerWithReplyHandler: ...");
            @try { ((void(*)(id, SEL, id))objc_msgSend)(proxy, sel_registerName("reloadFromServerWithReplyHandler:"), reloadReply);
                   if (dispatch_semaphore_wait(s1, dispatch_time(DISPATCH_TIME_NOW, (int64_t)(15*NSEC_PER_SEC))) != 0)
                       egglog(@"  [entitled] reload TIMEOUT"); }
            @catch (NSException *ex) { egglog(@"  [entitled] reload threw %@", ex.reason); }

            dispatch_semaphore_t s2 = dispatch_semaphore_create(0);
            void (^getReply)(id) = ^(id updates) {
                @autoreleasepool { logUpdateList(@"entitled getUpdates", updates); dispatch_semaphore_signal(s2); }
            };
            egglog(@"  [entitled] getUpdatesWithReplyHandler: ...");
            @try { ((void(*)(id, SEL, id))objc_msgSend)(proxy, sel_registerName("getUpdatesWithReplyHandler:"), getReply);
                   if (dispatch_semaphore_wait(s2, dispatch_time(DISPATCH_TIME_NOW, (int64_t)(15*NSEC_PER_SEC))) != 0)
                       egglog(@"  [entitled] getUpdates TIMEOUT"); }
            @catch (NSException *ex) { egglog(@"  [entitled] getUpdates threw %@", ex.reason); }

            dispatch_semaphore_t s3 = dispatch_semaphore_create(0);
            void (^metaReply)(id) = ^(id meta) {
                egglog(@"  [entitled] egginc metadata = %@ (class %@)", meta, meta ? NSStringFromClass([meta class]) : @"nil");
                dispatch_semaphore_signal(s3);
            };
            egglog(@"  [entitled] getUpdateMetadataForBundleID:com.auxbrain.egginc ...");
            @try { ((void(*)(id, SEL, id, id))objc_msgSend)(proxy, sel_registerName("getUpdateMetadataForBundleID:withReplyHandler:"),
                                                            @"com.auxbrain.egginc", metaReply);
                   if (dispatch_semaphore_wait(s3, dispatch_time(DISPATCH_TIME_NOW, (int64_t)(15*NSEC_PER_SEC))) != 0)
                       egglog(@"  [entitled] metadata TIMEOUT"); }
            @catch (NSException *ex) { egglog(@"  [entitled] metadata threw %@", ex.reason); }

            egglog(@"entitledRemote: done");
        }
    });
}

static void dumpClassFull(const char *clsName) {
    Class c = NSClassFromString([NSString stringWithUTF8String:clsName]);
    if (!c) { egglog(@"  class %s NOT present", clsName); return; }
    egglog(@"  class %s instance methods:", clsName);
    unsigned int n = 0;
    Method *m = class_copyMethodList(c, &n);
    for (unsigned int i = 0; i < n; i++) egglog(@"    -[%s %s]", clsName, sel_getName(method_getName(m[i])));
    free(m);
    unsigned int cn = 0;
    Method *cm = class_copyMethodList(object_getClass(c), &cn);
    for (unsigned int i = 0; i < cn; i++) egglog(@"    +[%s %s]", clsName, sel_getName(method_getName(cm[i])));
    free(cm);
}

static void probeSSPurchase(void) {
    egglog(@"probeSS: enumerating StoreKit purchase classes");
    const char *classes[] = {
        "SSPurchase", "SSPurchaseRequest", "SSPurchaseResponse", "SSPurchaseManager",
        "ISStoreClient", "SSDownloadManager", "ASDStoreClient", "SSVPurchase",
    };
    for (unsigned int i = 0; i < sizeof(classes)/sizeof(classes[0]); i++)
        dumpClassFull(classes[i]);
}

static void lockScreenViaSpringBoardServices(void) {
    void *h = dlopen("/System/Library/PrivateFrameworks/SpringBoardServices.framework/SpringBoardServices", RTLD_LAZY);
    if (!h) { egglog(@"  [lock] dlopen SpringBoardServices failed (%s)", dlerror()); return; }
    void (*lockFn)(void) = (void(*)(void))dlsym(h, "SBSLockDevice");
    if (lockFn) { egglog(@"  [lock] SBSLockDevice()"); lockFn(); }
    else { egglog(@"  [lock] SBSLockDevice symbol not found (%s)", dlerror()); }
}

static void firePurchaseUpdate(void) {
    dispatch_async(dispatch_get_global_queue(DISPATCH_QUEUE_PRIORITY_DEFAULT, 0), ^{
        @autoreleasepool {
            Class SSPurchase = NSClassFromString(@"SSPurchase");
            Class SSPurchaseRequest = NSClassFromString(@"SSPurchaseRequest");
            if (!SSPurchase || !SSPurchaseRequest) { egglog(@"  purchase classes missing"); return; }

            NSString *bp = [NSString stringWithFormat:
                @"productType=C&salableAdamId=%lld&pricingParameters=STDRDL&pg=default&price=0&hasBeenAuthedForBuy=true",
                kEggIncAdamId];
            id purchase = ((id(*)(id, SEL, id))objc_msgSend)(SSPurchase, sel_registerName("purchaseWithBuyParameters:"), bp);
            if (!purchase) { egglog(@"  purchaseWithBuyParameters: nil"); return; }
            ((void(*)(id, SEL, BOOL))objc_msgSend)(purchase, sel_registerName("setCreatesDownloads:"), YES);
            ((void(*)(id, SEL, BOOL))objc_msgSend)(purchase, sel_registerName("setCreatesInstallJobs:"), YES);
            ((void(*)(id, SEL, BOOL))objc_msgSend)(purchase, sel_registerName("setUsesLocalRedownloadParametersIfPossible:"), YES);
            egglog(@"  built SSPurchase buyParameters=%@", bp);

            id req = ((id(*)(id, SEL))objc_msgSend)(((id(*)(id, SEL))objc_msgSend)(SSPurchaseRequest, sel_registerName("alloc")), sel_registerName("init"));
            req = ((id(*)(id, SEL, id))objc_msgSend)(req, sel_registerName("initWithPurchases:"), @[purchase]);
            if (!req) { egglog(@"  SSPurchaseRequest nil"); return; }
            ((void(*)(id, SEL, BOOL))objc_msgSend)(req, sel_registerName("setCreatesDownloads:"), YES);
            ((void(*)(id, SEL, BOOL))objc_msgSend)(req, sel_registerName("setCreatesJobs:"), YES);

            dispatch_semaphore_t sem = dispatch_semaphore_create(0);
            void (^done)(id) = ^(id response) {
                @autoreleasepool {
                    id e = response ? ((id(*)(id, SEL))objc_msgSend)(response, sel_registerName("error")) : nil;
                    egglog(@"  [PURCHASE] completion response=%@ error=%@", response, e);
                    dispatch_semaphore_signal(sem);
                }
            };
            egglog(@"  [PURCHASE] startWithCompletionBlock: (adam %lld STDRDL) ...", kEggIncAdamId);
            @try { ((void(*)(id, SEL, id))objc_msgSend)(req, sel_registerName("startWithCompletionBlock:"), done);
                   if (dispatch_semaphore_wait(sem, dispatch_time(DISPATCH_TIME_NOW, (int64_t)(25*NSEC_PER_SEC))) != 0)
                       egglog(@"  [PURCHASE] start TIMEOUT (download may still proceed async in storedownloadd)"); }
            @catch (NSException *ex) { egglog(@"  [PURCHASE] start threw %@", ex.reason); }
            egglog(@"  [PURCHASE] done");

            lockScreenViaSpringBoardServices();
        }
    });
}

static void onTrigger(void) {
    @autoreleasepool {
        egglog(@"trigger fired");
        probeSSPurchase();
        probeRemoteProtocol();
        probeEntitledRemote();
#if EGGUPDATE_ARMED
        egglog(@"EGGUPDATE_ARMED=1: firing SSPurchase update");
        firePurchaseUpdate();
#else
        egglog(@"EGGUPDATE_ARMED=0: SSPurchase install withheld");
        (void)firePurchaseUpdate;
#endif
        fetchUpdatesThen(^(id eggUpdate, NSArray *all) {
            installUpdate(eggUpdate);
        });
        CFRunLoopRunInMode(kCFRunLoopDefaultMode, 52.0, false);
        egglog(@"onTrigger runloop spin done");
    }
}

static dispatch_source_t gSource;
static void installTriggerWatch(void) {
    int fd = open([kTriggerPath fileSystemRepresentation], O_CREAT | O_RDONLY, 0644);
    if (fd < 0) { egglog(@"cannot open trigger path %@ (errno %d)", kTriggerPath, errno); return; }
    gSource = dispatch_source_create(DISPATCH_SOURCE_TYPE_VNODE, fd,
                                     DISPATCH_VNODE_WRITE | DISPATCH_VNODE_ATTRIB | DISPATCH_VNODE_EXTEND,
                                     dispatch_get_global_queue(DISPATCH_QUEUE_PRIORITY_DEFAULT, 0));
    dispatch_source_set_event_handler(gSource, ^{ onTrigger(); });
    dispatch_source_set_cancel_handler(gSource, ^{ close(fd); });
    dispatch_resume(gSource);
    egglog(@"trigger watch armed on %@", kTriggerPath);
}

static void installInstallObserver(void) {
    [[NSNotificationCenter defaultCenter] addObserverForName:@"SBInstalledApplicationsDidChangeNotification"
                                                      object:nil queue:nil
                                                  usingBlock:^(NSNotification *note) {
        egglog(@"SBInstalledApplicationsDidChange");
    }];
}

static NSString *const kArmPath = @"/var/mobile/eggupdate.armed";

static void runIfArmed(void) {
    @autoreleasepool {
        if (![[NSFileManager defaultManager] fileExistsAtPath:kArmPath]) {
            egglog(@"launched but not armed (%@ absent); no-op", kArmPath);
            return;
        }
        [[NSFileManager defaultManager] removeItemAtPath:kArmPath error:nil];
        egglog(@"armed: running update flow on App Store launch");
        onTrigger();
    }
}

%ctor {
    @autoreleasepool {
        egglog(@"eggupdate loaded in %@ pid %d (ARMED=%d)",
               [[NSProcessInfo processInfo] processName], getpid(), EGGUPDATE_ARMED);
        installTriggerWatch();
        installInstallObserver();
        dispatch_after(dispatch_time(DISPATCH_TIME_NOW, (int64_t)(1.0 * NSEC_PER_SEC)),
                       dispatch_get_main_queue(), ^{ runIfArmed(); });
    }
}
