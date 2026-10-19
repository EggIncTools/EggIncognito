TARGET := iphone:clang:16.5:14.0
ARCHS = arm64
THEOS_PACKAGE_SCHEME = rootless

include $(THEOS)/makefiles/common.mk

TWEAK_NAME = eggupdate
eggupdate_FILES = Tweak.x
eggupdate_CFLAGS = -fobjc-arc
eggupdate_FRAMEWORKS = Foundation CoreFoundation

ifeq ($(EGGUPDATE_ARMED),1)
eggupdate_CFLAGS += -DEGGUPDATE_ARMED=1
endif

include $(THEOS)/makefiles/tweak.mk
