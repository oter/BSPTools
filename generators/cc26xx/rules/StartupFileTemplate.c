/*
	This file contains the entry point (Reset_Handler) of your firmware project.
	The reset handled initializes the RAM and calls system library initializers as well as
	the platform-specific initializer and the main() function.
*/
#include <driverlib/setup.h>

void Reset_Handler();
void Default_Handler();

#ifdef DEBUG_DEFAULT_INTERRUPT_HANDLERS
void __attribute__ ((weak)) $$VECTOR$$() $@+7
{
	//If you hit the breakpoint below, one of the interrupts was unhandled in your code. 
	//Define the following function in your code to handle it:
	//	extern "C" void $$VECTOR$$();
	asm("bkpt 255");
};

#else
void $$VECTOR$$() $$ALIGN_SPACE_OFFSET$$ __attribute__ ((weak, alias ("Default_Handler")));
#endif

extern void *_etext; 
extern void *_sdata; 
extern void *_edata;
extern void *_sbss; 
extern void *_ebss;
extern void *_estack;

void * g_pfnVectors[$$VECTOR_TABLE_SIZE$$] __attribute__ ((section (".isr_vector"))) = 
{
	&_estack,
	&Reset_Handler,
	$$VECTOR_POINTER$$,
};

extern void trimDevice(void);
extern void __libc_init_array();
extern int main();

void __attribute__((naked, noreturn)) Reset_Handler()
{
	$$EXTRA_RESET_HANDLER_CODE$$
	//Normally the CPU should will setup the based on the value from the first entry in the vector table.
	//If you encounter problems with accessing stack variables during initialization, ensure 
	//asm ("ldr sp, =_estack");

	trimDevice();

	void **pSource, **pDest;
	for (pSource = &_etext, pDest = &_sdata; pDest != &_edata; pSource++, pDest++)
		*pDest = *pSource;
	
	__asm("    ldr     r0, =_sbss\n"
          "    ldr     r1, =_ebss\n"
          "    mov     r2, #0\n"
          "    .thumb_func\n"
          "zero_loop:\n"
          "        cmp     r0, r1\n"
          "        it      lt\n"
          "        strlt   r2, [r0], #4\n"
          "        blt     zero_loop");

	__libc_init_array();
	main();
	FaultISR();
}

void __attribute__((naked, noreturn)) Default_Handler()
{
	//If you get stuck here, your code is missing a handler for some interrupt.
	//Define a 'DEBUG_DEFAULT_INTERRUPT_HANDLERS' macro via VisualGDB Project Properties and rebuild your project.
	//This will pinpoint a specific missing vector.
	for (;;) ;
}
