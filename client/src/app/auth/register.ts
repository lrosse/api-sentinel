import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { finalize, switchMap } from 'rxjs';
import { apiErrorMessage } from '../shared/api-error';
import { AuthService } from './auth.service';

@Component({
  selector: 'app-register',
  imports: [FormsModule, RouterLink],
  templateUrl: './register.html',
})
export class Register {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected email = '';
  protected password = '';
  protected readonly submitting = signal(false);
  protected readonly error = signal<string | null>(null);

  protected submit(): void {
    this.error.set(null);
    this.submitting.set(true);
    this.auth
      .register(this.email, this.password)
      .pipe(
        switchMap(() => this.auth.login(this.email, this.password)),
        finalize(() => this.submitting.set(false)),
      )
      .subscribe({
        next: () => void this.router.navigate(['/catalog']),
        error: (error: unknown) =>
          this.error.set(apiErrorMessage(error, 'Não foi possível criar sua conta.')),
      });
  }
}
