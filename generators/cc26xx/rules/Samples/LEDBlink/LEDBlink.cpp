#include <gpio.h>
#include <ioc.h>
#include <driverlib/sys_ctrl.h>

#define LED_IO_$$com.sysprogs.examples.ledblink.LEDPIN$$ IOID_$$com.sysprogs.examples.ledblink.LEDPIN$$

int main(void)
{	
	SysCtrlPowerEverything();
	IOCPinTypeGpioOutput(LED_IO_$$com.sysprogs.examples.ledblink.LEDPIN$$);
	GPIO_writeDio(LED_IO_$$com.sysprogs.examples.ledblink.LEDPIN$$, 1);

	while (1)
	{
		CPUdelay($$com.sysprogs.examples.ledblink.DELAY$$);
		GPIO_toggleDio(LED_IO_$$com.sysprogs.examples.ledblink.LEDPIN$$);
	}
}
