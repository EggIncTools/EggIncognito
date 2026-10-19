TARGET := iphone:clang:16.5:14.0
ARCHS = arm64
THEOS_PACKAGE_SCHEME = rootless

include $(THEOS)/makefiles/common.mk

TWEAK_NAME = egiuinav
egiuinav_FILES = Tweak.xm
egiuinav_CFLAGS = -fobjc-arc
egiuinav_FRAMEWORKS = UIKit Foundation CoreFoundation QuartzCore IOKit

include $(THEOS)/makefiles/tweak.mk
